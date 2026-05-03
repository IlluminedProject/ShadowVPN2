using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hysteria2InboundConfig), "hysteria2")]
public abstract class InboundConfig
{
    [JsonPropertyName("tag")] public string Tag { get; set; } = null!;

    [JsonPropertyName("listen")] public string? Listen { get; set; }

    [JsonPropertyName("listen_port")] public int? ListenPort { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DirectOutboundConfig), "direct")]
public abstract class OutboundConfig
{
    [JsonPropertyName("tag")] public string Tag { get; set; } = null!;
}

public class InboundTlsConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    [JsonPropertyName("server_name")] public string? ServerName { get; set; }

    [JsonPropertyName("certificate_path")] public string? CertificatePath { get; set; }

    [JsonPropertyName("key_path")] public string? KeyPath { get; set; }

    [JsonPropertyName("certificate")] public List<string>? Certificate { get; set; }

    [JsonPropertyName("key")] public List<string>? Key { get; set; }
}