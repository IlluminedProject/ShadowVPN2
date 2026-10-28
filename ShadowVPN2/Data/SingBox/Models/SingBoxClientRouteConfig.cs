using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class SingBoxClientRouteConfig {
    [JsonPropertyName("final")] public string Final { get; set; } = null!;
}
