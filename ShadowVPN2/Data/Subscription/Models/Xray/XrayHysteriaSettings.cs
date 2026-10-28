using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayHysteriaSettings {
    [JsonPropertyName("version")] public int Version { get; set; } = 2;

    [JsonPropertyName("address")] public string Address { get; set; } = null!;

    [JsonPropertyName("port")] public int Port { get; set; }
}
