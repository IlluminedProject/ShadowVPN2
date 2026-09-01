using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public class WireGuardEndpointConfig : EndpointConfig {
    [JsonPropertyName("system")] public bool System { get; set; } = true;

    [JsonPropertyName("name")] public string Name { get; set; } = "wg0";

    [JsonPropertyName("mtu")] public int? Mtu { get; set; }

    [JsonPropertyName("address")] public List<string> Address { get; set; } = new();

    [JsonPropertyName("private_key")] public string PrivateKey { get; set; } = null!;

    [JsonPropertyName("listen_port")] public int ListenPort { get; set; }

    [JsonPropertyName("peers")] public List<WireGuardPeer> Peers { get; set; } = new();
}

public class AwgEndpointConfig : EndpointConfig {
    [JsonPropertyName("useIntegratedTun")] public bool UseIntegratedTun { get; set; } = false;

    [JsonPropertyName("mtu")] public int? Mtu { get; set; }

    [JsonPropertyName("address")] public List<string> Address { get; set; } = new();

    [JsonPropertyName("private_key")] public string PrivateKey { get; set; } = null!;

    [JsonPropertyName("listen_port")] public int ListenPort { get; set; }

    [JsonPropertyName("peers")] public List<WireGuardPeer> Peers { get; set; } = new();

    [JsonPropertyName("jc")] public int? Jc { get; set; }

    [JsonPropertyName("jmin")] public int? Jmin { get; set; }

    [JsonPropertyName("jmax")] public int? Jmax { get; set; }

    [JsonPropertyName("s1")] public int? S1 { get; set; }

    [JsonPropertyName("s2")] public int? S2 { get; set; }

    [JsonPropertyName("h1")] public string? H1 { get; set; }

    [JsonPropertyName("h2")] public string? H2 { get; set; }

    [JsonPropertyName("h3")] public string? H3 { get; set; }

    [JsonPropertyName("h4")] public string? H4 { get; set; }
}

public class WireGuardPeer {
    [JsonPropertyName("public_key")] public string PublicKey { get; set; } = null!;

    [JsonPropertyName("allowed_ips")] public List<string> AllowedIps { get; set; } = new();

    [JsonPropertyName("persistent_keepalive_interval")]
    public int? PersistentKeepaliveInterval { get; set; }

    [JsonPropertyName("address")] public string? Address { get; set; }

    [JsonPropertyName("port")] public int? Port { get; set; }
}