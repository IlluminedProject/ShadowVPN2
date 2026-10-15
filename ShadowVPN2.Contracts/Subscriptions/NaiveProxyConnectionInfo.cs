using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Subscriptions;

public sealed class NaiveProxyConnectionInfo : ProtocolConnectionInfo {
    public required HostName ServerAddress { get; set; }
    public int ServerPort { get; set; }
    public bool Quic { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
    public required HostName Sni { get; set; }
    public bool Insecure { get; set; }
    public string? PinSha256 { get; set; }
}