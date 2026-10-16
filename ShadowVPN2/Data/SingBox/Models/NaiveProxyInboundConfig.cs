using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public sealed class NaiveProxyInboundConfig : InboundConfig, IConditionallyEnabledConfig {
    [JsonPropertyName("network")] public string Network { get; set; } = "tcp";

    [JsonPropertyName("users")] public List<NaiveProxyUser> Users { get; set; } = [];

    [JsonPropertyName("tls")] public InboundTlsConfig? Tls { get; set; } = new();

    bool IConditionallyEnabledConfig.IsActuallyEnabled {
        get => Users.Count > 0;
    }
}