using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Subscription;

public sealed class FreeTurnConnectionInfo : TransportConnectionInfo {
    public const int DefaultClientListenPort = 9000;

    private static readonly JsonSerializerOptions SerializerOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public required string Peer { get; init; }

    public required FreeTurnObfuscationProfile? ObfuscationProfile { get; init; }

    public required string? ObfuscationKey { get; init; }

    public required FreeTurnTurnTransport TurnTransport { get; init; }

    public required ProtocolSocketKind Mode { get; init; }

    public required int Streams { get; init; }

    public required string ClientId { get; init; }

    public string CreateShareUrl(string clientName, string? vpnConfig = null) {
        var payload = new Payload {
            Provider = "vk",
            Peer = Peer,
            Transport = TurnTransport == FreeTurnTurnTransport.Udp ? "udp" : null,
            Mode = Mode == ProtocolSocketKind.Tcp ? "tcp" : null,
            Obf = ObfuscationProfile?.ToCommandLineValue(),
            Key = ObfuscationKey,
            N = Streams,
            ClientId = ClientId,
            Name = clientName,
            WireGuardConfig = vpnConfig
        };

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"freeturn://{encoded}";
    }

    public string CreateLaunchCommand(string clientPath, string vkCallLink) {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vkCallLink);
        return $"{clientPath} \"{CreateShareUrl(string.Empty)}\" -link \"{vkCallLink}\"";
    }

    private sealed class Payload {
        [JsonPropertyName("v")] public int Version { get; init; } = 1;
        [JsonPropertyName("provider")] public required string Provider { get; init; }
        [JsonPropertyName("peer")] public required string Peer { get; init; }
        [JsonPropertyName("transport")] public string? Transport { get; init; }
        [JsonPropertyName("mode")] public string? Mode { get; init; }
        [JsonPropertyName("obf")] public string? Obf { get; init; }
        [JsonPropertyName("key")] public string? Key { get; init; }
        [JsonPropertyName("n")] public int N { get; init; }
        [JsonPropertyName("cid")] public required string ClientId { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("wg")] public string? WireGuardConfig { get; init; }
    }
}