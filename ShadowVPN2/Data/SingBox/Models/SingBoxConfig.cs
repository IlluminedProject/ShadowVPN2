using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class SingBoxConfig
{
    [JsonPropertyName("log")] public SingBoxLogConfig Log { get; set; } = new();

    [JsonPropertyName("inbounds")] public List<InboundConfig> Inbounds { get; set; } = new();

    [JsonPropertyName("outbounds")] public List<OutboundConfig> Outbounds { get; set; } = new();

    [JsonPropertyName("route")] public SingBoxRouteConfig? Route { get; set; }
}

public class SingBoxLogConfig
{
    [JsonPropertyName("level")] public string Level { get; set; } = "info";
}

public class SingBoxRouteConfig
{
    [JsonPropertyName("rules")] public List<object> Rules { get; set; } = new();
}