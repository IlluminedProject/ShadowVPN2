namespace ShadowVPN2.Data.Subscription;

public class Hysteria2ConnectionInfo : ProtocolConnectionInfo
{
    public required string ServerAddress { get; set; }
    public int ServerPort { get; set; }
    public required string Password { get; set; }
    public string? ObfsType { get; set; }
    public string? ObfsPassword { get; set; }
    public string? Sni { get; set; }
    public string? PinSHA256 { get; set; }
    public required string ShareUrl { get; set; }
}