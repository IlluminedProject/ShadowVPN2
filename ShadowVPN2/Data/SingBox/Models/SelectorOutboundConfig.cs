using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class SelectorOutboundConfig : OutboundConfig {
    [JsonPropertyName("outbounds")] public List<string> Outbounds { get; set; } = new();
}
