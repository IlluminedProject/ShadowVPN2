using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayUdpMaskConfig {
    [JsonPropertyName("type")] public string Type { get; set; } = null!;

    [JsonPropertyName("settings")] public XrayUdpMaskSettings Settings { get; set; } = new();
}
