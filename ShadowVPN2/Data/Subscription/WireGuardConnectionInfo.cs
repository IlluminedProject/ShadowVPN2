using System.Text;

namespace ShadowVPN2.Data.Subscription;

public sealed class WireGuardConnectionInfo : ProtocolConnectionInfo {
    public required string ServerAddress { get; set; }
    public required int ServerPort { get; set; }
    public required string PrivateKey { get; set; }
    public required string AssignedIp { get; set; }
    public required string ServerPublicKey { get; set; }
    public required int Mtu { get; set; }
    public required bool IsAmneziaWg { get; set; }
    public int Jc { get; set; }
    public int Jmin { get; set; }
    public int Jmax { get; set; }
    public int S1 { get; set; }
    public int S2 { get; set; }
    public int S3 { get; set; }
    public int S4 { get; set; }
    public required string H1 { get; set; }
    public required string H2 { get; set; }
    public required string H3 { get; set; }
    public required string H4 { get; set; }
    public string? I1 { get; set; }
    public string? I2 { get; set; }
    public string? I3 { get; set; }
    public string? I4 { get; set; }
    public string? I5 { get; set; }

    public string CreateConfig() {
        var config =
            $"""
             [Interface]
             PrivateKey = {PrivateKey}
             Address = {AssignedIp}/32
             MTU = {Mtu}

             [Peer]
             PublicKey = {ServerPublicKey}
             Endpoint = {FormatHost(ServerAddress)}:{ServerPort}
             AllowedIPs = 0.0.0.0/0, ::/0
             PersistentKeepalive = 25

             """;
        if (!IsAmneziaWg)
            return config;

        config +=
            $"""
             Jc = {Jc}
             Jmin = {Jmin}
             Jmax = {Jmax}
             S1 = {S1}
             S2 = {S2}
             S3 = {S3}
             S4 = {S4}
             H1 = {H1}
             H2 = {H2}
             H3 = {H3}
             H4 = {H4}

             """;
        return config
               + Optional("I1", I1)
               + Optional("I2", I2)
               + Optional("I3", I3)
               + Optional("I4", I4)
               + Optional("I5", I5);
    }

    public string CreateShareUrl() {
        return $"wireguard://{Convert.ToBase64String(Encoding.UTF8.GetBytes(CreateConfig()))}";
    }

    private static string FormatHost(string host) {
        return host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
    }

    private static string Optional(string name, string? value) {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{name} = {value}\n";
    }
}