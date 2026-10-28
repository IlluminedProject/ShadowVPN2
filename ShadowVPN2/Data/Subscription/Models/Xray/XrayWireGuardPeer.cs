using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayWireGuardPeer {
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = null!;

    [JsonPropertyName("publicKey")] public string PublicKey { get; set; } = null!;

    [JsonPropertyName("allowedIPs")] public List<string> AllowedIPs { get; set; } = [];
}
