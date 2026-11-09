using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using ShadowVPN2.Updater.Docker;

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

    public async Task<DockerContainer?> InspectAsync(string name, CancellationToken token) {
        _logger.LogDebug("Docker inspect container {Container}", name);
        using var response = await _http.GetAsync($"containers/{Uri.EscapeDataString(name)}/json", token);
        if (response.StatusCode == HttpStatusCode.NotFound) {
            _logger.LogDebug("Docker container not found {Container}", name);
            return null;
        }

        return await ReadAsync(response, DockerJsonContext.Default.DockerContainer, token);
    }

    public async Task<DockerImage> InspectImageAsync(string id, CancellationToken token) {
        _logger.LogDebug("Docker inspect image {Image}", id);
        using var response = await _http.GetAsync($"images/{Uri.EscapeDataString(id)}/json", token);
        return await ReadAsync(response, DockerJsonContext.Default.DockerImage, token);
    }

    public async Task<DockerCreateResponse> CreateContainerAsync(string path, DockerCreateRequest body,
        CancellationToken token) {
        _logger.LogDebug("Docker create container: {Path}", path);
        using var content = new StringContent(
            JsonSerializer.Serialize(body, DockerJsonContext.Default.DockerCreateRequest), Encoding.UTF8,
            "application/json");
        using var response = await _http.PostAsync(path, content, token);
        return await ReadAsync(response, DockerJsonContext.Default.DockerCreateResponse, token);
    }

    public async Task PostActionAsync(string id, string action, DockerRestartPolicy? body, CancellationToken token) {
        _logger.LogDebug("Docker action {Action} on container {Id}", action, id);
        using var content = body is not null
            ? new StringContent(JsonSerializer.Serialize(body, DockerJsonContext.Default.DockerRestartPolicy),
                Encoding.UTF8,
                "application/json")
            : null;
        using var response = await _http.PostAsync($"containers/{Uri.EscapeDataString(id)}/{action}", content, token);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAsync(string id, CancellationToken token) {
        _logger.LogDebug("Docker remove container {Container}", id);
        using var response =
            await _http.DeleteAsync($"containers/{Uri.EscapeDataString(id)}?force=true&v=false", token);
        response.EnsureSuccessStatusCode();
    }

    public async Task PullAsync(string image, CancellationToken token) {
        _logger.LogInformation("Docker pull started for {Image}", image);
        var stopwatch = Stopwatch.StartNew();
        using var request =
            new HttpRequestMessage(HttpMethod.Post, $"images/create?fromImage={Uri.EscapeDataString(image)}");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) {
            var errorText = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException($"Docker API {(int)response.StatusCode}: {errorText}");
        }

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        while (await reader.ReadLineAsync(token) is { } line) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var message = JsonNode.Parse(line)!;
            var error = message["error"]?.GetValue<string>() ?? message["errorDetail"]?["message"]?.GetValue<string>();
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
        var result = await ReadAsync(inspect, DockerJsonContext.Default.DockerImage, token);
        if (image.Contains('@') && !result.RepoDigests.Contains(image, StringComparer.Ordinal))
            throw new InvalidOperationException("Pulled image does not resolve to the requested digest");
        _logger.LogInformation("Docker pull completed for {Image} in {ElapsedMilliseconds} ms", image,
            stopwatch.ElapsedMilliseconds);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo,
        CancellationToken token) {
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Docker API {(int)response.StatusCode}: {text}");
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Docker API returned empty response");
        return JsonSerializer.Deserialize(text, typeInfo) ??
               throw new InvalidOperationException("Docker API response could not be deserialized");
    }
}