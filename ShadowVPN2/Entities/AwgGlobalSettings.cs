using System.Security.Cryptography;

namespace ShadowVPN2.Entities;

public class AwgGlobalSettings : IAmneziaWgParameters {
    public int ListenPort { get; set; } = 51820;
    public int H1 { get; set; } = RandomNumberGenerator.GetInt32(1, 2147483647);
    public int H2 { get; set; } = RandomNumberGenerator.GetInt32(1, 2147483647);
    public int H3 { get; set; } = RandomNumberGenerator.GetInt32(1, 2147483647);
    public int H4 { get; set; } = RandomNumberGenerator.GetInt32(1, 2147483647);
    public int Jc { get; set; } = RandomNumberGenerator.GetInt32(3, 10);
    public int Jmin { get; set; } = RandomNumberGenerator.GetInt32(15, 150);
    public int Jmax { get; set; } = RandomNumberGenerator.GetInt32(500, 2000);
    public int S1 { get; set; } = RandomNumberGenerator.GetInt32(10, 100);
    public int S2 { get; set; } = RandomNumberGenerator.GetInt32(10, 100);
    public int S3 { get; set; }
    public int S4 { get; set; }
    public string? I1 { get; set; }
    public string? I2 { get; set; }
    public string? I3 { get; set; }
    public string? I4 { get; set; }
    public string? I5 { get; set; }
}