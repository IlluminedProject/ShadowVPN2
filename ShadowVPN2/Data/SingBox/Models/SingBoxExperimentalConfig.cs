using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class SingBoxExperimentalConfig {
    [JsonPropertyName("cache_file")] public SingBoxCacheFileConfig? CacheFile { get; set; }

    [JsonPropertyName("clash_api")] public SingBoxClashApiConfig? ClashApi { get; set; }
}

public class SingBoxCacheFileConfig {
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
}

public class SingBoxClashApiConfig {
    [JsonPropertyName("external_controller")]
    public string ExternalController { get; set; } = null!;

    [JsonPropertyName("secret")] public string? Secret { get; set; }
}