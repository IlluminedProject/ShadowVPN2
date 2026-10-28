using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class ClientTlsConfig {
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    [JsonPropertyName("server_name")] public string? ServerName { get; set; }

    [JsonPropertyName("certificate_sha256")] public List<string>? CertificateSha256 { get; set; }
}
