using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayWireGuardOutboundConfig : XrayOutboundConfig {
    [JsonPropertyName("settings")] public XrayWireGuardSettings Settings { get; set; } = new();
}
