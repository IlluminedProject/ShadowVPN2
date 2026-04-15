using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ShadowVPN2.Infrastructure.Configurations;

public static class RavenDbCertificates
{
    public static X509Certificate2 GenerateRootCa()
    {
        using var rsa = RSA.Create(4096);
        var req = new CertificateRequest(
            "CN=ShadowVPN-Cluster-Root-CA, O=ShadowVPN",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(100));
    }

    public static (RSA Rsa, CertificateRequest Request) GenerateCertificateRequest()
    {
        var rsa = RSA.Create(4096);
        var req = new CertificateRequest(
            $"CN=ShadowVPN-Cluster-Root-CA, O=ShadowVPN",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign |
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1"), // Server Auth
                new Oid("1.3.6.1.5.5.7.3.2") // Client Auth
            }, false));

        return (rsa, req);
    }

    public static X509Certificate2 SignCertificate(CertificateRequest req, X509Certificate2 cert)
    {
        var serialNumber = new byte[8];
        RandomNumberGenerator.Fill(serialNumber);
        return req.Create(cert, cert.NotBefore, cert.NotAfter, serialNumber);
    }

    public static void TrustCustomRootCa(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        if (!store.Certificates.Contains(certificate))
        {
            store.Add(certificate);
        }
    }
}