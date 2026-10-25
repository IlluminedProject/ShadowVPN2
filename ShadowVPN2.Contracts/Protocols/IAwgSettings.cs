namespace ShadowVPN2.Contracts.Protocols;

public interface IAwgSettings {
    uint Jc { get; set; }
    uint Jmin { get; set; }
    uint Jmax { get; set; }
    int S1 { get; set; }
    int S2 { get; set; }
    int S3 { get; set; }
    int S4 { get; set; }
    string H1 { get; set; }
    string H2 { get; set; }
    string H3 { get; set; }
    string H4 { get; set; }
    string? I1 { get; set; }
    string? I2 { get; set; }
    string? I3 { get; set; }
    string? I4 { get; set; }
    string? I5 { get; set; }
}