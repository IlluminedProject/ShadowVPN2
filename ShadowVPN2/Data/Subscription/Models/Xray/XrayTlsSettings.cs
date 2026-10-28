using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

public sealed class XrayTlsSettings {
    [JsonPropertyName("serverName")] public string? ServerName { get; set; }

    [JsonPropertyName("alpn")] public List<string> Alpn { get; set; } = [];

    [JsonPropertyName("pinnedPeerCertSha256")] public string? PinnedPeerCertSha256 { get; set; }
}
