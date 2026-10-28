using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayWireGuardSettings {
    [JsonPropertyName("secretKey")] public string SecretKey { get; set; } = null!;

    [JsonPropertyName("address")] public List<string> Address { get; set; } = [];

    [JsonPropertyName("mtu")] public int Mtu { get; set; }

    [JsonPropertyName("peers")] public List<XrayWireGuardPeer> Peers { get; set; } = [];
}
