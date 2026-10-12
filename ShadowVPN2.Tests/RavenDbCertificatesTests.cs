using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using ShadowVPN2.Infrastructure.Configurations;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class RavenDbCertificatesTests {
    [Fact]
    public async Task Signed_join_certificate_should_authenticate_the_assigned_mesh_ip() {
        using var root = RavenDbCertificates.GenerateRootCa();
        var (issuerKey, issuerRequest) = RavenDbCertificates.GenerateCertificateRequest(IPAddress.Parse("100.64.0.11"));
        using var issuerRsa = issuerKey;
        var distinctIssuerRequest = new CertificateRequest("CN=ShadowVPN-Test-Issuer", issuerRsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        foreach (var extension in issuerRequest.CertificateExtensions)
            distinctIssuerRequest.CertificateExtensions.Add(extension);
        using var issuerPublic = RavenDbCertificates.SignCertificate(distinctIssuerRequest, root);
        using var issuer = issuerPublic.CopyWithPrivateKey(issuerRsa);
        var (nodeKey, nodeRequest) = RavenDbCertificates.GenerateCertificateRequest();
        using var nodeRsa = nodeKey;
        using var signed = RavenDbCertificates.SignCertificateFromPem(nodeRequest.CreateSigningRequestPem(), issuer,
            IPAddress.Parse("100.64.0.12"));
        using var certificateWithKey = signed.CopyWithPrivateKey(nodeRsa);
        using var serverCertificate = X509CertificateLoader.LoadPkcs12(
            certificateWithKey.Export(X509ContentType.Pfx), null);

        serverCertificate.MatchesHostname("100.64.0.12").Should().BeTrue();
        serverCertificate.MatchesHostname("100.64.0.13").Should().BeFalse();
        serverCertificate.MatchesHostname("localhost").Should().BeTrue();
        issuer.MatchesHostname("100.64.0.11").Should().BeTrue();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        var connect = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, timeout.Token);
        using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
        await connect;
        using var serverTls = new SslStream(accepted.GetStream());
        using var clientTls = new SslStream(client.GetStream());
        var serverHandshake = serverTls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
            ServerCertificateContext = SslStreamCertificateContext.Create(serverCertificate,
                new X509Certificate2Collection(issuer), true)
        }, timeout.Token);
        var clientHandshake = clientTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = "100.64.0.12",
            CertificateChainPolicy = new X509ChainPolicy {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                CustomTrustStore = { root },
                ExtraStore = { issuer },
                RevocationMode = X509RevocationMode.NoCheck
            }
        }, timeout.Token);
        await Task.WhenAll(serverHandshake, clientHandshake);
        await serverTls.WriteAsync(new byte[] { 42 }, timeout.Token);
        var received = new byte[1];
        (await clientTls.ReadAsync(received, timeout.Token)).Should().Be(1);
        received[0].Should().Be(42);
    }
}