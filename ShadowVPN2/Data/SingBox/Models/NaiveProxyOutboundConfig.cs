using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class NaiveProxyOutboundConfig : OutboundConfig {
    [JsonPropertyName("server")] public string Server { get; set; } = null!;

    [JsonPropertyName("server_port")] public int ServerPort { get; set; }

    [JsonPropertyName("username")] public string Username { get; set; } = null!;

    [JsonPropertyName("password")] public string Password { get; set; } = null!;

    [JsonPropertyName("quic")] public bool Quic { get; set; }

    [JsonPropertyName("udp_over_tcp")] public bool UdpOverTcp { get; set; }

    [JsonPropertyName("tls")] public ClientTlsConfig? Tls { get; set; }
}
