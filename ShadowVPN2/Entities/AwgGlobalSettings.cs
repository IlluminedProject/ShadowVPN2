using System.Security.Cryptography;
using System.Text.Json.Serialization;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Entities;

public class AwgGlobalSettings : ProtocolGlobalSettings, IProtocolDefinition, IAwgSettings {
    public override string Protocol {
        get => "WireGuard";
    }

    public override PortBinding ListenPort { get; set; } = new(51820);
    public int Mtu { get; set; } = 1408;
    public string ServerAddress { get; set; } = "100.64.0.1/10";
    public string PrivateKey { get; set; } = GenerateKey();

    [JsonIgnore]
    public bool IsAmneziaWg {
        get => Jc.HasValue || Jmin.HasValue || Jmax.HasValue
               || S1.HasValue || S2.HasValue || S3.HasValue || S4.HasValue
               || !string.IsNullOrWhiteSpace(H1) || !string.IsNullOrWhiteSpace(H2)
               || !string.IsNullOrWhiteSpace(H3) || !string.IsNullOrWhiteSpace(H4)
               || !string.IsNullOrWhiteSpace(I1) || !string.IsNullOrWhiteSpace(I2)
               || !string.IsNullOrWhiteSpace(I3) || !string.IsNullOrWhiteSpace(I4)
               || !string.IsNullOrWhiteSpace(I5);
    }

    public string? H1 { get; set; }

    public string? H2 { get; set; }

    public string? H3 { get; set; }

    public string? H4 { get; set; }

    public uint? Jc { get; set; }
    public uint? Jmin { get; set; }
    public uint? Jmax { get; set; }
    public int? S1 { get; set; }
    public int? S2 { get; set; }
    public int? S3 { get; set; }
    public int? S4 { get; set; }
    public string? I1 { get; set; }
    public string? I2 { get; set; }
    public string? I3 { get; set; }
    public string? I4 { get; set; }
    public string? I5 { get; set; }

    public static ProtocolSocketKind SocketKind {
        get => ProtocolSocketKind.Udp;
    }

    public static AwgGlobalSettings CreateForCluster() {
        return new AwgGlobalSettings {
            PrivateKey = string.Empty,
            H1 = RandomHeaderValue(),
            H2 = RandomHeaderValue(),
            H3 = RandomHeaderValue(),
            H4 = RandomHeaderValue(),
            Jc = (uint)RandomNumberGenerator.GetInt32(3, 10),
            Jmin = (uint)RandomNumberGenerator.GetInt32(15, 150),
            Jmax = (uint)RandomNumberGenerator.GetInt32(500, 2000),
            S1 = RandomNumberGenerator.GetInt32(10, 100),
            S2 = RandomNumberGenerator.GetInt32(10, 100)
        };
    }

    private static string RandomHeaderValue() {
        return RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString();
    }

    private static string GenerateKey() {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}