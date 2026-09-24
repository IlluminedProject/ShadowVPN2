namespace ShadowVPN2.Entities;

public interface IAmneziaWgParameters {
    int Jc { get; set; }
    int Jmin { get; set; }
    int Jmax { get; set; }
    int S1 { get; set; }
    int S2 { get; set; }
    int S3 { get; set; }
    int S4 { get; set; }
    string? I1 { get; set; }
    string? I2 { get; set; }
    string? I3 { get; set; }
    string? I4 { get; set; }
    string? I5 { get; set; }
}