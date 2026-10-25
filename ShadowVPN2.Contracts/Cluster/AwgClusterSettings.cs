using ShadowVPN2.Contracts.Protocols;

namespace ShadowVPN2.Contracts.Cluster;

public sealed class AwgClusterSettings : IAwgSettings {
    public int ListenPort { get; set; }
    public string H1 { get; set; } = string.Empty;
    public string H2 { get; set; } = string.Empty;
    public string H3 { get; set; } = string.Empty;
    public string H4 { get; set; } = string.Empty;
    public uint Jc { get; set; }
    public uint Jmin { get; set; }
    public uint Jmax { get; set; }
    public int S1 { get; set; }
    public int S2 { get; set; }
    public int S3 { get; set; }
    public int S4 { get; set; }
    public string? I1 { get; set; }
    public string? I2 { get; set; }
    public string? I3 { get; set; }
    public string? I4 { get; set; }
    public string? I5 { get; set; }
}