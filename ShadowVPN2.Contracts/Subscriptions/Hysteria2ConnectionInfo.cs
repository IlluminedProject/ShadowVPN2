using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Subscriptions;

public sealed class Hysteria2ConnectionInfo : ProtocolConnectionInfo {
    public required HostName ServerAddress { get; set; }
    public PortBinding PortBinding { get; set; }
    public required string Password { get; set; }
    public string? ObfsType { get; set; }
    public string? ObfsPassword { get; set; }
    public HostName? Sni { get; set; }
    public string? PinSha256 { get; set; }
}