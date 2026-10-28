using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XraySubscriptionConfig {
    [JsonPropertyName("outbounds")] public List<XrayOutboundConfig> Outbounds { get; set; } = [];
}
