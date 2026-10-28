using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class SingBoxClientConfig {
    [JsonPropertyName("outbounds")] public List<OutboundConfig> Outbounds { get; set; } = new();

    [JsonPropertyName("route")] public SingBoxClientRouteConfig Route { get; set; } = new();

    [JsonPropertyName("endpoints")] public List<EndpointConfig>? Endpoints { get; set; }
}
