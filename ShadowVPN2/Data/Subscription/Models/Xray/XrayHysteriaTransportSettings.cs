using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayHysteriaTransportSettings {
    [JsonPropertyName("version")] public int Version { get; set; } = 2;

    [JsonPropertyName("auth")] public string Auth { get; set; } = null!;
}
