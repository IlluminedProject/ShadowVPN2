using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ManagedCertificateServiceTests(RavenServerFixture server) : IClassFixture<RavenServerFixture> {
    [RavenServerFact]
    public async Task Per_domain_certificates_are_persisted_separately_and_legacy_SAN_material_is_not_loaded() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var nodeId = Guid.NewGuid();
        var node = HostName.Parse("node.example.com");
        var protocol = HostName.Parse("protocol.example.com");
        var aggregate = CreateCertificate(node, protocol);
        using (var session = store.OpenAsyncSession()) {
            await session.StoreAsync(new EntityManagedCertificate {
                Id = $"ManagedCertificates/{nodeId:D}", NodeId = nodeId,
                CertificatePem = aggregate.Certificate, PrivateKeyPem = aggregate.Key
            }, timeout.Token);
            await session.SaveChangesAsync(timeout.Token);
        }

        using var certificates = new ManagedCertificateService(store, NullLogger<ManagedCertificateService>.Instance);
        await certificates.StartAsync(timeout.Token);
        try {
            Assert.Null(certificates.GetForDomain(node));
            var nodePem = CreateCertificate(node);
            var protocolPem = CreateCertificate(protocol);
            await certificates.SaveAsync(nodeId, [node], nodePem.Certificate, nodePem.Key, timeout.Token);
            await certificates.SaveAsync(nodeId, [protocol], protocolPem.Certificate, protocolPem.Key, timeout.Token,
                1);
            var materials = certificates.GetForNodeDomains(nodeId);
            Assert.Equal(new[] { node, protocol }, materials.Select(material => material.Domains.Single()));
            Assert.Equal(nodePem.Certificate, certificates.GetForDomain(node)!.CertificatePem);
            Assert.Equal(protocolPem.Certificate, certificates.GetForDomain(protocol)!.CertificatePem);
            using var session = store.OpenAsyncSession();
            Assert.Null(
                await session.LoadAsync<EntityManagedCertificate>($"ManagedCertificates/{nodeId:D}", timeout.Token));
            var entities = await certificates.GetEntitiesAsync(nodeId, timeout.Token);
            Assert.Equal(2, entities.Count);
            Assert.All(entities, entity => Assert.NotNull(entity.Domain));
            await Assert.ThrowsAsync<InvalidOperationException>(() => certificates.SaveAsync(nodeId, [node],
                aggregate.Certificate, aggregate.Key, timeout.Token));
        }
        finally {
            await certificates.StopAsync(timeout.Token);
        }
    }

    [RavenServerFact]
    public async Task Reuse_moves_only_the_matching_single_domain_certificate() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        using var certificates = new ManagedCertificateService(store, NullLogger<ManagedCertificateService>.Instance);
        var firstNode = Guid.NewGuid();
        var secondNode = Guid.NewGuid();
        var first = HostName.Parse("one.example.com");
        var second = HostName.Parse("two.example.com");
        var firstPem = CreateCertificate(first);
        var secondPem = CreateCertificate(second);
        await certificates.SaveAsync(firstNode, [first], firstPem.Certificate, firstPem.Key, timeout.Token);
        await certificates.SaveAsync(firstNode, [second], secondPem.Certificate, secondPem.Key, timeout.Token);
        Assert.True(await certificates.TryReuseAsync(secondNode, [second], timeout.Token));
        Assert.Single(await certificates.GetEntitiesAsync(firstNode, timeout.Token));
        var moved = Assert.Single(await certificates.GetEntitiesAsync(secondNode, timeout.Token));
        Assert.Equal(second, moved.Domain);
        Assert.Equal(secondPem.Certificate, moved.CertificatePem);
    }

    private static (string Certificate, string Key) CreateCertificate(params HostName[] domains) {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={domains[0]}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var domain in domains) san.AddDnsName(domain.ToString()!);
        request.CertificateExtensions.Add(san.Build());
        using var certificate =
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(90));
        return (certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());
    }
}