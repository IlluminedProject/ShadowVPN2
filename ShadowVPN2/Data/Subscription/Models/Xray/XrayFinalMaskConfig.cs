using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayFinalMaskConfig {
    [JsonPropertyName("udp")] public List<XrayUdpMaskConfig> Udp { get; set; } = [];
}
