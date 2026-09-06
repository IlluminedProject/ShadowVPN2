using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Entities;

public class WireGuardAmneziaGlobalSettings : ProtocolGlobalSettings {
    public override string Protocol {
        get => "WireGuard";
    }

    public int ListenPort { get; set; } = 51820;
    public int Mtu { get; set; } = 1408;
    public string ServerAddress { get; set; } = "100.64.0.1/10";
    public string PrivateKey { get; set; } = GenerateKey();

    public int Jc { get; set; }
    public int Jmin { get; set; }
    public int Jmax { get; set; }
    public int S1 { get; set; }
    public int S2 { get; set; }
    public int S3 { get; set; }
    public int S4 { get; set; }
    public string H1 { get; set; } = "1";
    public string H2 { get; set; } = "2";
    public string H3 { get; set; } = "3";
    public string H4 { get; set; } = "4";
    public string? I1 { get; set; }
    public string? I2 { get; set; }
    public string? I3 { get; set; }
    public string? I4 { get; set; }
    public string? I5 { get; set; }

    [JsonIgnore]
    public bool IsAmneziaWg {
        get => Jc != 0 || Jmin != 0 || Jmax != 0 || S1 != 0 || S2 != 0 || S3 != 0 || S4 != 0
               || H1 != "1" || H2 != "2" || H3 != "3" || H4 != "4"
               || !string.IsNullOrWhiteSpace(I1) || !string.IsNullOrWhiteSpace(I2)
               || !string.IsNullOrWhiteSpace(I3) || !string.IsNullOrWhiteSpace(I4)
               || !string.IsNullOrWhiteSpace(I5);
    }

    private static string GenerateKey() {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}