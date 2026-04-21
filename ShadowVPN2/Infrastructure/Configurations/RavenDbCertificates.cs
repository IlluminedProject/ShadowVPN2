using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;

namespace ShadowVPN2.Infrastructure.Configurations;

public static class RavenDbCertificates
{
    private static readonly Serilog.ILogger Logger = Log.ForContext(typeof(RavenDbCertificates));

    public static X509Certificate2 GenerateRootCa()
    {
        Logger.Information("Generating new Root CA certificate");
        using var rsa = RSA.Create(4096);
        var req = new CertificateRequest(
            "CN=ShadowVPN-Cluster-Root-CA, O=ShadowVPN",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(100));
        Logger.Information("Root CA certificate generated successfully (Thumbprint: {Thumbprint})", cert.Thumbprint);
        return cert;
    }

    public static (RSA Rsa, CertificateRequest Request) GenerateCertificateRequest()
    {
        Logger.Information("Generating node certificate request");
        var rsa = RSA.Create(4096);
        var req = new CertificateRequest(
            "CN=ShadowVPN-Cluster-Node, O=ShadowVPN",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1"), // Server Auth
                new Oid("1.3.6.1.5.5.7.3.2") // Client Auth
            }, false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("*");
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddDnsName(System.Net.Dns.GetHostName());
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        sanBuilder.AddIpAddress(System.Net.IPAddress.Any);
        sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Any);
        req.CertificateExtensions.Add(sanBuilder.Build());

        Logger.Debug("Node certificate request generated");
        return (rsa, req);
    }

    public static X509Certificate2 SignCertificate(CertificateRequest req, X509Certificate2 cert)
    {
        Logger.Information("Signing node certificate with Root CA");
        var serialNumber = new byte[8];
        RandomNumberGenerator.Fill(serialNumber);
        var signedCert = req.Create(cert, cert.NotBefore, cert.NotAfter, serialNumber);
        Logger.Information("Node certificate signed successfully (Thumbprint: {Thumbprint})", signedCert.Thumbprint);
        return signedCert;
    }

    public static void TrustCustomRootCa(X509Certificate2 certificate)
    {
        Logger.Information("Trusting custom Root CA (Thumbprint: {Thumbprint})", certificate.Thumbprint);
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        if (!store.Certificates.Contains(certificate))
        {
            store.Add(certificate);
            Logger.Information("Custom Root CA added to the current user Root store");
        }
        else
        {
            Logger.Debug("Custom Root CA is already trusted");
        }
    }
}