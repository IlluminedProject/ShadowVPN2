using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ShadowVPN2.Infrastructure.Configurations;

public static class RavenDbCertificates {
    public static X509Certificate2 GenerateNodeCertificate(IPAddress meshIp) {
        using var rsa = RSA.Create(4096);
        var request = new CertificateRequest($"CN=ShadowVPN-Node-{meshIp}, O=ShadowVPN", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection {
            new Oid("1.3.6.1.5.5.7.3.1"),
            new Oid("1.3.6.1.5.5.7.3.2")
        }, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        san.AddIpAddress(meshIp);
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
    }

    public static X509Certificate2 LoadNodeCertificate(string certificatePem, IPAddress meshIp) {
        var certificate = X509Certificate2.CreateFromPem(certificatePem);
        try {
            var constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
            var usage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
            var san = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().SingleOrDefault();
            if (certificate.HasPrivateKey || constraints is not { CertificateAuthority: false }
                                          || usage == null || san == null ||
                                          !san.EnumerateIPAddresses().Contains(meshIp)
                                          || !usage.EnhancedKeyUsages.Cast<Oid>()
                                              .Any(oid => oid.Value == "1.3.6.1.5.5.7.3.1")
                                          || !usage.EnhancedKeyUsages.Cast<Oid>()
                                              .Any(oid => oid.Value == "1.3.6.1.5.5.7.3.2")
                                          || !certificate.SubjectName.RawData.AsSpan()
                                              .SequenceEqual(certificate.IssuerName.RawData)) {
                throw new ArgumentException(
                    "A self-signed node certificate with client/server usage and the assigned mesh IP is required.");
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(certificate);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            if (!chain.Build(certificate) || chain.ChainElements.Count != 1)
                throw new ArgumentException("The node certificate signature or validity is invalid.");
            return certificate;
        }
        catch {
            certificate.Dispose();
            throw;
        }
    }

    public static bool MatchesPin(X509Certificate2? certificate, X509Certificate2 pin) {
        return certificate != null && certificate.RawData.AsSpan().SequenceEqual(pin.RawData)
                                   && certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow
                                   && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow;
    }
}