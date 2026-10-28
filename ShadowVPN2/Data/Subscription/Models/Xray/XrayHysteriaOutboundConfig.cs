using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayHysteriaOutboundConfig : XrayOutboundConfig {
    [JsonPropertyName("settings")] public XrayHysteriaSettings Settings { get; set; } = new();

    [JsonPropertyName("streamSettings")] public XrayHysteriaStreamSettings StreamSettings { get; set; } = new();
}
