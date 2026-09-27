namespace ShadowVPN2.Contracts.Subscriptions;

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
}