using System.Security.Cryptography;

namespace ShadowVPN2.Entities;

public class AwgGlobalSettings
{
    public AwgGlobalSettings()
    {
        Jc = RandomNumberGenerator.GetInt32(3, 10);
        Jmin = RandomNumberGenerator.GetInt32(15, 150);
        Jmax = RandomNumberGenerator.GetInt32(500, 2000);
        S1 = RandomNumberGenerator.GetInt32(10, 100);
        S2 = RandomNumberGenerator.GetInt32(10, 100);
        H1 = RandomNumberGenerator.GetInt32(1, 2147483647);
        H2 = RandomNumberGenerator.GetInt32(1, 2147483647);
        H3 = RandomNumberGenerator.GetInt32(1, 2147483647);
        H4 = RandomNumberGenerator.GetInt32(1, 2147483647);
    }

    public int ListenPort { get; set; } = 51820;
    public int Jc { get; set; }
    public int Jmin { get; set; }
    public int Jmax { get; set; }
    public int S1 { get; set; }
    public int S2 { get; set; }
    public int H1 { get; set; }
    public int H2 { get; set; }
    public int H3 { get; set; }
    public int H4 { get; set; }
}