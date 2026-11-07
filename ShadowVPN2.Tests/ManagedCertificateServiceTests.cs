using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ManagedCertificateServiceTests(RavenServerFixture server) : IClassFixture<RavenServerFixture> {
    [RavenServerFact]
    public async Task Reuses_existing_certificate_when_domains_move_to_new_node() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var oldNodeId = Guid.NewGuid();
        var newNodeId = Guid.NewGuid();
        using var store = await server.CreateStoreAsync(timeout.Token);
        var domain = HostName.Parse("edge.example.test");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=edge.example.test", key, HashAlgorithmName.SHA256);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(domain.Value);
        request.CertificateExtensions.Add(sanBuilder.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));
        var certificatePem = certificate.ExportCertificatePem();
        var keyPem = key.ExportPkcs8PrivateKeyPem();
        using (var session = store.OpenAsyncSession()) {
            await session.StoreAsync(new EntityManagedCertificate {
                Id = $"ManagedCertificates/{oldNodeId:D}", NodeId = oldNodeId,
                Domains = [domain], CertificatePem = certificatePem, PrivateKeyPem = keyPem,
                NotBefore = certificate.NotBefore.ToUniversalTime(), NotAfter = certificate.NotAfter.ToUniversalTime()
            });
            await session.SaveChangesAsync(timeout.Token);
        }

        using var service = new ManagedCertificateService(store, NullLogger<ManagedCertificateService>.Instance);
        await service.StartAsync(timeout.Token);

        (await service.TryReuseAsync(newNodeId, [domain], timeout.Token)).Should().BeTrue();
        service.GetCertificate(domain).Should().NotBeNull();
        var adopted = await service.GetEntityAsync(newNodeId, timeout.Token);
        adopted.Should().NotBeNull();
        adopted!.Domains.Should().ContainSingle().Which.Should().Be(domain);
        (await service.GetEntityAsync(oldNodeId, timeout.Token)).Should().BeNull();
    }
}