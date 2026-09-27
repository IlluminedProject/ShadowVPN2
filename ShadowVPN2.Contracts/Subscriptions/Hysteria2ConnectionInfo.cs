namespace ShadowVPN2.Contracts.Subscriptions;

public sealed class Hysteria2ConnectionInfo : ProtocolConnectionInfo {
    public required string ServerAddress { get; set; }
    public int ServerPort { get; set; }
    public required string Password { get; set; }
    public string? ObfsType { get; set; }
    public string? ObfsPassword { get; set; }
    public string? Sni { get; set; }
    public string? PinSha256 { get; set; }
}