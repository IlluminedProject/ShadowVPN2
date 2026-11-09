using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ShadowVPN2.Infrastructure.RavenDb.RavenEmbedded;
using Xunit;

namespace ShadowVPN2.Infrastructure.RavenDb.Tests;

public sealed class EmbeddedServerCertificateValidationTests {
    [Theory]
    [InlineData(SslPolicyErrors.None, true)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable, false)]
    public void Remote_peer_requires_platform_trust_and_matching_hostname(SslPolicyErrors errors, bool expected) {
        using var certificate = CreateCertificate("peer");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://100.64.0.11:8888");
        // Even presenting the local certificate cannot bypass hostname validation for a remote target.
        Assert.Equal(expected, ValidateServerCertificate(request, certificate, errors, certificate.Thumbprint));
    }

    [Theory]
    [InlineData("https://127.0.0.1:8888", true)]
    [InlineData("https://localhost:8888", true)]
    [InlineData("https://[::1]:8888", true)]
    [InlineData("https://100.64.0.11:8888", false)]
    public void Local_certificate_pin_only_bypasses_errors_for_loopback(string url, bool expected) {
        using var certificate = CreateCertificate("mesh");
        using var otherCertificate = CreateCertificate("other");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var errors = SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors;
        Assert.Equal(expected, ValidateServerCertificate(request, certificate, errors, certificate.Thumbprint));
        Assert.False(ValidateServerCertificate(request, otherCertificate, errors, certificate.Thumbprint));
        Assert.False(ValidateServerCertificate(new object(), certificate, errors, certificate.Thumbprint));
        Assert.False(ValidateServerCertificate(request, null, SslPolicyErrors.None, certificate.Thumbprint));
    }

    private static bool ValidateServerCertificate(object sender, X509Certificate? certificate,
        SslPolicyErrors errors, string thumbprint) {
        var method = typeof(EmbeddedServer).GetMethod("ValidateServerCertificate",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, [sender, certificate, errors, thumbprint])!;
    }

    private static X509Certificate2 CreateCertificate(string name) {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(name);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection {
            new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")
        }, false));
        using var generated =
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
    }
}