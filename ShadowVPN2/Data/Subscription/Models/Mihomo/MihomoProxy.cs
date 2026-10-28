namespace ShadowVPN2.Data.Subscription.Models.Mihomo;

public sealed class MihomoProxy(string name, string type) {
    public string Name { get; } = name;
    public string Type { get; } = type;
    public required string Server { get; init; }
    public int Port { get; init; }
    public string? Password { get; init; }
    public string? Username { get; init; }
    public string? Sni { get; init; }
    public bool? SkipCertVerify { get; init; }
    public string? Fingerprint { get; init; }
    public string? Obfs { get; init; }
    public string? ObfsPassword { get; init; }
    public string? Ports { get; init; }
    public string? Ip { get; init; }
    public string? PrivateKey { get; init; }
    public string? PublicKey { get; init; }
    public int? Mtu { get; init; }
    public IReadOnlyList<string>? AllowedIps { get; init; }
    public bool? Udp { get; init; }
}
