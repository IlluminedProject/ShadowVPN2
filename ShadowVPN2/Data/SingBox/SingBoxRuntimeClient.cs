using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox;

/// <summary>
///     Typed client for the sing-box Clash API runtime management routes
///     (SPEC 019/152): live replacement of inbound users and WireGuard peers
///     without restarting the instance.
/// </summary>
public sealed class SingBoxRuntimeClient : IDisposable {
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;
    private readonly ILogger<SingBoxRuntimeClient> logger;

    private SingBoxRuntimeClient(HttpClient http, ILogger<SingBoxRuntimeClient> logger) {
        this.http = http;
        this.logger = logger;
    }

    public SingBoxRuntimeClient(SingBoxRuntimeEndpoint endpoint, ILogger<SingBoxRuntimeClient> logger)
        : this($"http://{endpoint.Address}", endpoint.Secret, logger) {
    }

    public SingBoxRuntimeClient(string baseAddress, string secret, ILogger<SingBoxRuntimeClient> logger)
        : this(CreateHttpClient(baseAddress, secret), logger) {
    }

    public void Dispose() {
        http.Dispose();
    }

    private static HttpClient CreateHttpClient(string baseAddress, string? secret) {
        var client = new HttpClient { BaseAddress = new Uri(baseAddress) };
        if (!string.IsNullOrEmpty(secret))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    public async Task<IReadOnlyList<RuntimeUserDto>> GetInboundUsersAsync(string tag, CancellationToken ct = default) {
        var response = await http.GetAsync($"runtime/inbounds/{Uri.EscapeDataString(tag)}/users", ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<UsersResponse>(SerializerOptions, ct)
            .ConfigureAwait(false);
        return payload?.Users ?? [];
    }

    public async Task<IReadOnlyList<RuntimeUserDto>> ReplaceInboundUsersAsync(string tag,
        IReadOnlyList<RuntimeUserDto> users, CancellationToken ct = default) {
        var response = await http.PutAsJsonAsync($"runtime/inbounds/{Uri.EscapeDataString(tag)}/users",
            new UsersResponse { Users = users.ToList() }, SerializerOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<UsersResponse>(SerializerOptions, ct)
            .ConfigureAwait(false);
        return payload?.Users ?? [];
    }

    public async Task<IReadOnlyList<WireGuardPeerDto>>
        GetEndpointPeersAsync(string tag, CancellationToken ct = default) {
        var response = await http.GetAsync($"runtime/endpoints/{Uri.EscapeDataString(tag)}/peers", ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<PeersResponse>(SerializerOptions, ct)
            .ConfigureAwait(false);
        return payload?.Peers ?? [];
    }

    public async Task<IReadOnlyList<WireGuardPeerDto>> ReplaceEndpointPeersAsync(string tag,
        IReadOnlyList<WireGuardPeerDto> peers, CancellationToken ct = default) {
        var response = await http.PutAsJsonAsync($"runtime/endpoints/{Uri.EscapeDataString(tag)}/peers",
            new PeersResponse { Peers = peers.ToList() }, SerializerOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<PeersResponse>(SerializerOptions, ct)
            .ConfigureAwait(false);
        return payload?.Peers ?? [];
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct) {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        logger.LogWarning("sing-box runtime API returned {StatusCode}: {Body}", (int)response.StatusCode, body);
        throw new SingBoxRuntimeException($"runtime API returned {(int)response.StatusCode}: {body}");
    }

    public sealed class SingBoxRuntimeException(string message) : Exception(message);

    public sealed class UsersResponse {
        [JsonPropertyName("users")] public List<RuntimeUserDto> Users { get; set; } = [];
    }

    public sealed class PeersResponse {
        [JsonPropertyName("peers")] public List<WireGuardPeerDto> Peers { get; set; } = [];
    }
}

public sealed class RuntimeUserDto {
    [JsonPropertyName("username")] public string Username { get; set; } = null!;
    [JsonPropertyName("password")] public string? Password { get; set; }
}

public sealed class WireGuardPeerDto {
    [JsonPropertyName("public_key")] public string PublicKey { get; set; } = null!;
    [JsonPropertyName("allowed_ips")] public List<string> AllowedIps { get; set; } = [];

    [JsonPropertyName("persistent_keepalive_interval")]
    public int? PersistentKeepaliveInterval { get; set; }

    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("port")] public int? Port { get; set; }
    [JsonPropertyName("pre_shared_key")] public string? PreSharedKey { get; set; }
    [JsonPropertyName("reserved")] public List<int>? Reserved { get; set; }
}