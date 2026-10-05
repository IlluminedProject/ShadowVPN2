using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ShadowVPN2.Entities;

public sealed class NaiveProxyGlobalSettings : ProtocolGlobalSettings, IProtocolDefinition,
    IProtocolSettingsCertificateRegeneratable {
    public override string Protocol {
        get => "NaiveProxy";
    }

    public override int ListenPort { get; set; } = 443;
    public string TlsCertificatePem { get; set; } = string.Empty;
    public string TlsKeyPem { get; set; } = string.Empty;

    public static ProtocolSocketKind SocketKind {
        get => ProtocolSocketKind.Tcp;
    }

    public void GenerateSelfSignedCertificate() {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=ShadowVPN-NaiveProxy", ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("shadowvpn.local");
        request.CertificateExtensions.Add(sanBuilder.Build());

        using var certificate =
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        TlsCertificatePem = certificate.ExportCertificatePem();
        TlsKeyPem = ecdsa.ExportPkcs8PrivateKeyPem();
    }

    public string? GetCertificateFingerprint() {
        if (string.IsNullOrWhiteSpace(TlsCertificatePem)) return null;
        try {
            using var certificate = X509Certificate2.CreateFromPem(TlsCertificatePem);
            return BitConverter.ToString(certificate.GetCertHash(HashAlgorithmName.SHA256)).Replace("-", ":");
        }
        catch {
            return null;
        }
    }
}