namespace ShadowVPN2.Contracts.Subscriptions;

public sealed class NaiveProxyConnectionInfo : ProtocolConnectionInfo {
    public required string ServerAddress { get; set; }
    public int ServerPort { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
    public required string Sni { get; set; }
    public bool Insecure { get; set; }
    public string? PinSha256 { get; set; }
}