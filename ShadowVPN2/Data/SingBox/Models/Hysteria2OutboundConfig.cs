using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class Hysteria2OutboundConfig : OutboundConfig {
    [JsonPropertyName("server")] public string Server { get; set; } = null!;

    [JsonPropertyName("server_port")] public int? ServerPort { get; set; }

    [JsonPropertyName("server_ports")] public List<string>? ServerPorts { get; set; }

    [JsonPropertyName("password")] public string Password { get; set; } = null!;

    [JsonPropertyName("obfs")] public Hysteria2ObfsConfig? Obfs { get; set; }

    [JsonPropertyName("tls")] public ClientTlsConfig? Tls { get; set; }
}
