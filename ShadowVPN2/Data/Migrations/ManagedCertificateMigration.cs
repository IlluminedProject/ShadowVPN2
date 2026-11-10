using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents.Session;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.RavenDb.Migrations;

namespace ShadowVPN2.Data.Migrations;

public sealed class ManagedCertificateMigration : IRavenMigration {
    public int Version {
        get => 4;
    }

    public string Description {
        get => "Split aggregate managed certificates into per-domain records";
    }

    public async Task ApplyAsync(IAsyncDocumentSession session, CancellationToken cancellationToken) {
        var legacy = await session.Advanced.AsyncDocumentQuery<EntityManagedCertificate>()
            .WhereEquals(nameof(EntityManagedCertificate.Domain), null)
            .ToListAsync(cancellationToken);
        foreach (var entity in legacy) {
            if (string.IsNullOrWhiteSpace(entity.CertificatePem) || string.IsNullOrWhiteSpace(entity.PrivateKeyPem)) {
                session.Delete(entity);
                continue;
            }

            using var certificate = X509Certificate2.CreateFromPem(entity.CertificatePem, entity.PrivateKeyPem);
            var dnsNames = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>()
                .SelectMany(extension => extension.EnumerateDnsNames()).ToArray();
            if (dnsNames.Length != 1) {
                session.Delete(entity);
                continue;
            }

            var domain = entity.Domains.FirstOrDefault(candidate =>
                string.Equals(candidate.ToString(), dnsNames[0], StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(domain.ToString())) {
                session.Delete(entity);
                continue;
            }

            var id = ManagedCertificateService.GetId(entity.NodeId, domain);
            var existing = await session.LoadAsync<EntityManagedCertificate>(id, cancellationToken);
            if (existing is null) {
                await session.StoreAsync(new EntityManagedCertificate {
                    Id = id,
                    NodeId = entity.NodeId,
                    Domain = domain,
                    DomainOrder = entity.Domains.Count > 0 && entity.Domains[0] == domain ? 0 : 1,
                    Domains = [domain],
                    CertificatePem = entity.CertificatePem,
                    PrivateKeyPem = entity.PrivateKeyPem,
                    NotBefore = entity.NotBefore,
                    NotAfter = entity.NotAfter,
                    LastAttemptAt = entity.LastAttemptAt,
                    LastError = entity.LastError
                }, id, cancellationToken);
            }

            session.Delete(entity);
        }

        await session.SaveChangesAsync(cancellationToken);
    }
}