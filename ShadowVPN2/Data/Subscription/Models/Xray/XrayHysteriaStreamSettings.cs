using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayHysteriaStreamSettings {
    [JsonPropertyName("network")] public string Network { get; set; } = "hysteria";

    [JsonPropertyName("security")] public string Security { get; set; } = "tls";

    [JsonPropertyName("hysteriaSettings")] public XrayHysteriaTransportSettings HysteriaSettings { get; set; } = new();

    [JsonPropertyName("tlsSettings")] public XrayTlsSettings TlsSettings { get; set; } = new();

    [JsonPropertyName("finalmask")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public XrayFinalMaskConfig? FinalMask { get; set; }
}
