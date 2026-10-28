using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayUdpMaskSettings {
    [JsonPropertyName("password")] public string? Password { get; set; }
}
