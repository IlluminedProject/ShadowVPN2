using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class Hysteria2InboundConfig : InboundConfig
{
    [JsonPropertyName("users")] public List<Hysteria2User> Users { get; set; } = new();

    [JsonPropertyName("obfs")] public Hysteria2ObfsConfig? Obfs { get; set; }

    [JsonPropertyName("tls")] public InboundTlsConfig? Tls { get; set; }
}

public class Hysteria2User
{
    [JsonPropertyName("name")] public string Name { get; set; } = null!;

    [JsonPropertyName("password")] public string Password { get; set; } = null!;
}

public class Hysteria2ObfsConfig
{
    [JsonPropertyName("type")] public string Type { get; set; } = "salamander";

    [JsonPropertyName("password")] public string Password { get; set; } = null!;
}