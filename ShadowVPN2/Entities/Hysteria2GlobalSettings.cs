using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ShadowVPN2.Entities;

public class Hysteria2GlobalSettings : ProtocolGlobalSettings
{
    public override string Protocol => "Hysteria2";
    public int ListenPort { get; set; } = 4443;
    public string ObfsType { get; set; } = "salamander";
    public string ObfsPassword { get; set; } = GeneratePassword();
    public string TlsCertificatePem { get; set; } = "";
    public string TlsKeyPem { get; set; } = "";

    public static string GeneratePassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    }

    public void GenerateSelfSignedCertificate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(
            "CN=ShadowVPN-Hysteria2",
            ecdsa,
            HashAlgorithmName.SHA256);

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("shadowvpn.local");
        req.CertificateExtensions.Add(sanBuilder.Build());

        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        TlsCertificatePem = cert.ExportCertificatePem();
        TlsKeyPem = ecdsa.ExportPkcs8PrivateKeyPem();
    }
}