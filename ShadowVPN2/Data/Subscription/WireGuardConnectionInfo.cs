namespace ShadowVPN2.Data.Subscription;

public sealed class WireGuardConnectionInfo : ProtocolConnectionInfo {
    public required string ServerAddress { get; set; }
    public required int ServerPort { get; set; }
    public required string Config { get; set; }
    public required string ShareUrl { get; set; }
    public required bool IsAmneziaWg { get; set; }
}