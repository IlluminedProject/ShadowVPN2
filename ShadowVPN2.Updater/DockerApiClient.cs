using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace ShadowVPN2.Updater;

public sealed class DockerApiClient : IDisposable {
    private readonly HttpClient _http;
    private readonly ILogger<DockerApiClient> _logger;

    public DockerApiClient(ILogger<DockerApiClient> logger) {
        _logger = logger;
        var handler = new SocketsHttpHandler {
            ConnectCallback = async (_, token) => {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/docker.sock"), token);
                    return new NetworkStream(socket, true);
                }
                catch {
                    socket.Dispose();
                    throw;
                }
            }
        };
        _http = new HttpClient(handler)
            { BaseAddress = new Uri("http://localhost/"), Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose() {
        _http.Dispose();
    }

    public async Task<JsonObject?> InspectAsync(string name, CancellationToken token) {
        _logger.LogDebug("Docker inspect container {Container}", name);
        using var response = await _http.GetAsync($"containers/{Uri.EscapeDataString(name)}/json", token);
        if (response.StatusCode == HttpStatusCode.NotFound) {
            _logger.LogDebug("Docker container not found {Container}", name);
            return null;
        }

        return await ReadAsync(response, token);
    }

    public async Task<JsonObject> InspectImageAsync(string id, CancellationToken token) {
        _logger.LogDebug("Docker inspect image {Image}", id);
        using var response = await _http.GetAsync($"images/{Uri.EscapeDataString(id)}/json", token);
        return await ReadAsync(response, token);
    }

    public async Task<JsonObject> SendAsync(string path, JsonObject? body, CancellationToken token) {
        _logger.LogDebug("Docker POST {Path}", path);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, token);
        return await ReadAsync(response, token);
    }

    public async Task RemoveAsync(string id, CancellationToken token) {
        _logger.LogDebug("Docker remove container {Container}", id);
        using var response =
            await _http.DeleteAsync($"containers/{Uri.EscapeDataString(id)}?force=true&v=false", token);
        await ReadAsync(response, token);
    }

    public async Task PullAsync(string image, CancellationToken token) {
        _logger.LogInformation("Docker pull started for {Image}", image);
        var stopwatch = Stopwatch.StartNew();
        using var request =
            new HttpRequestMessage(HttpMethod.Post, $"images/create?fromImage={Uri.EscapeDataString(image)}");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) {
            await ReadAsync(response, token);
            return;
        }

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        while (await reader.ReadLineAsync(token) is { } line) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var message = JsonNode.Parse(line)!;
            var error = message["error"]?.GetValue<string>() ??
                        message["errorDetail"]?["message"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(error)) {
                _logger.LogError("Docker pull reported an error for {Image}: {Error}", image, error);
                throw new InvalidOperationException($"Docker image pull failed: {error}");
            }

            if (message["status"] is JsonValue status) {
                _logger.LogDebug("Docker pull {Image}: {Status} {Progress}", image, status.GetValue<string>(),
                    message["progress"]?.GetValue<string>());
            }
        }

        using var inspect = await _http.GetAsync($"images/{Uri.EscapeDataString(image)}/json", token);
        var result = await ReadAsync(inspect, token);
        if (result["RepoDigests"] is not JsonArray digests || !digests.Any(x => x?.GetValue<string>() == image))
            throw new InvalidOperationException("Pulled image does not resolve to the requested digest");
        _logger.LogInformation("Docker pull completed for {Image} in {ElapsedMilliseconds} ms", image,
            stopwatch.ElapsedMilliseconds);
    }

    private static async Task<JsonObject> ReadAsync(HttpResponseMessage response, CancellationToken token) {
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Docker API {(int)response.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text)!.AsObject();
    }
}