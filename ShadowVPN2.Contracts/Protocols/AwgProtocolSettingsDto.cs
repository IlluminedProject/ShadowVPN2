namespace ShadowVPN2.Contracts.Protocols;

public sealed class AwgProtocolSettingsDto : ProtocolSettingsDto {
    public string H1 { get; set; } = "1";
    public string H2 { get; set; } = "2";
    public string H3 { get; set; } = "3";
    public string H4 { get; set; } = "4";
    public int Jc { get; set; }
    public int Jmin { get; set; }
    public int Jmax { get; set; }
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