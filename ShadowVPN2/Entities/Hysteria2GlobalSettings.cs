using System.Security.Cryptography;

namespace ShadowVPN2.Entities;

public class Hysteria2GlobalSettings : ProtocolGlobalSettings
{
    public override string Protocol => "Hysteria2";
    public int ListenPort { get; set; } = 4443;
    public string ObfsType { get; set; } = "salamander";
    public string ObfsPassword { get; set; } = GeneratePassword();
    public string? TlsCertificatePath { get; set; }
    public string? TlsKeyPath { get; set; }

    public static string GeneratePassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    }
}