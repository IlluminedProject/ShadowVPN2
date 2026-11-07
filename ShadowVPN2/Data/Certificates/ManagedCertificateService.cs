using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data.Certificates;

public sealed class ManagedCertificateService(
    IDocumentStore documentStore,
    ILogger<ManagedCertificateService> logger) : IHostedService, IDisposable {
    private readonly List<X509Certificate2> _certificates = [];
    private readonly Lock _lock = new();

    private IReadOnlyDictionary<HostName, X509Certificate2> _certificatesByDomain =
        new Dictionary<HostName, X509Certificate2>();

    private IReadOnlyDictionary<HostName, ManagedCertificateMaterial> _materialsByDomain =
        new Dictionary<HostName, ManagedCertificateMaterial>();

    private IReadOnlyDictionary<Guid, ManagedCertificateMaterial> _materialsByNode =
        new Dictionary<Guid, ManagedCertificateMaterial>();

    private IDisposable? _subscription;

    public void Dispose() {
        _subscription?.Dispose();
        foreach (var certificate in _certificates) certificate.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken) {
        await ReloadAsync(cancellationToken);
        _subscription = documentStore.Changes()
            .ForDocumentsInCollection<EntityManagedCertificate>()
            .Subscribe(new ActionObserver<DocumentChange>(change => {
                _ = ReloadAndNotifyAsync();
            }));
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }

    public event Func<Task>? CertificatesChanged;

    public X509Certificate2? GetCertificate(HostName? serverName) {
        if (serverName is not { } host) return null;
        lock (_lock)
            return _certificatesByDomain.GetValueOrDefault(host);
    }

    public ManagedCertificateMaterial? GetForDomain(HostName? domain) {
        if (domain is not { } host) return null;
        lock (_lock)
            return _materialsByDomain.GetValueOrDefault(host);
    }

    public ManagedCertificateMaterial? GetForNode(Guid nodeId) {
        lock (_lock)
            return _materialsByNode.GetValueOrDefault(nodeId);
    }

    public async Task<EntityManagedCertificate?> GetEntityAsync(Guid nodeId,
        CancellationToken cancellationToken = default) {
        using var session = documentStore.OpenAsyncSession();
        return await session.LoadAsync<EntityManagedCertificate>(GetId(nodeId), cancellationToken);
    }

    public async Task SaveAsync(Guid nodeId, IReadOnlyList<HostName> domains, string certificatePem,
        string privateKeyPem, CancellationToken cancellationToken = default) {
        using var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
        var normalizedDomains = domains.Distinct()
            .OrderBy(domain => domain.ToString(), StringComparer.Ordinal).ToList();
        var id = GetId(nodeId);
        using var session = documentStore.OpenAsyncSession();
        var entity = await session.LoadAsync<EntityManagedCertificate>(id, cancellationToken)
                     ?? new EntityManagedCertificate { Id = id, NodeId = nodeId };
        entity.Domains = normalizedDomains;
        entity.CertificatePem = certificatePem;
        entity.PrivateKeyPem = privateKeyPem;
        entity.NotBefore = certificate.NotBefore.ToUniversalTime();
        entity.NotAfter = certificate.NotAfter.ToUniversalTime();
        entity.LastAttemptAt = DateTimeOffset.UtcNow;
        entity.LastError = null;
        await session.StoreAsync(entity, id, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
        await ReloadAndNotifyAsync(cancellationToken);
    }

    public async Task<bool> TryReuseAsync(Guid nodeId, IReadOnlyList<HostName> domains,
        CancellationToken cancellationToken = default) {
        var normalizedDomains = domains.Distinct()
            .OrderBy(domain => domain.ToString(), StringComparer.Ordinal).ToList();
        if (normalizedDomains.Count == 0) return false;

        using var session = documentStore.OpenAsyncSession();
        var entities = await session.Query<EntityManagedCertificate>().ToListAsync(cancellationToken);
        var candidate = entities
            .Where(entity => entity.NodeId != nodeId &&
                             !string.IsNullOrWhiteSpace(entity.CertificatePem) &&
                             !string.IsNullOrWhiteSpace(entity.PrivateKeyPem))
            .Select(entity => new {
                Entity = entity,
                Domains = entity.Domains.Intersect(normalizedDomains).ToHashSet()
            })
            .Where(candidate => candidate.Domains.Count > 0)
            .OrderByDescending(candidate => candidate.Domains.Count)
            .ThenBy(candidate => candidate.Entity.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate is null) return false;

        var target = entities.FirstOrDefault(entity => entity.NodeId == nodeId);
        if (target is { CertificatePem.Length: > 0, PrivateKeyPem.Length: > 0 }) return false;
        target ??= new EntityManagedCertificate {
            Id = GetId(nodeId), NodeId = nodeId
        };

        target.Domains = candidate.Domains.OrderBy(domain => domain.ToString(), StringComparer.Ordinal).ToList();
        target.CertificatePem = candidate.Entity.CertificatePem;
        target.PrivateKeyPem = candidate.Entity.PrivateKeyPem;
        target.NotBefore = candidate.Entity.NotBefore;
        target.NotAfter = candidate.Entity.NotAfter;
        target.LastAttemptAt = candidate.Entity.LastAttemptAt;
        target.LastError = null;
        candidate.Entity.Domains = candidate.Entity.Domains
            .Where(domain => !candidate.Domains.Contains(domain)).ToList();

        await session.StoreAsync(target, target.Id, cancellationToken);
        if (candidate.Entity.Domains.Count == 0)
            session.Delete(candidate.Entity);
        await session.SaveChangesAsync(cancellationToken);
        await ReloadAndNotifyAsync(cancellationToken);
        return true;
    }

    public async Task RecordErrorAsync(Guid nodeId, string error, CancellationToken cancellationToken = default) {
        var id = GetId(nodeId);
        using var session = documentStore.OpenAsyncSession();
        var entity = await session.LoadAsync<EntityManagedCertificate>(id, cancellationToken)
                     ?? new EntityManagedCertificate { Id = id, NodeId = nodeId };
        entity.LastAttemptAt = DateTimeOffset.UtcNow;
        entity.LastError = error;
        await session.StoreAsync(entity, id, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task ReloadAndNotifyAsync(CancellationToken cancellationToken = default) {
        try {
            await ReloadAsync(cancellationToken);
            if (CertificatesChanged != null) await CertificatesChanged.Invoke();
        }
        catch (Exception ex) {
            logger.LogError(ex, "Failed to reload managed certificates");
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken) {
        using var session = documentStore.OpenAsyncSession();
        var entities = await session.Query<EntityManagedCertificate>().ToListAsync(cancellationToken);
        var byDomain = new Dictionary<HostName, X509Certificate2>();
        var materialsByDomain = new Dictionary<HostName, ManagedCertificateMaterial>();
        var byNode = new Dictionary<Guid, ManagedCertificateMaterial>();
        var loadedCertificates = new List<X509Certificate2>();

        foreach (var entity in entities.Where(entity => !string.IsNullOrWhiteSpace(entity.CertificatePem) &&
                                                        !string.IsNullOrWhiteSpace(entity.PrivateKeyPem))) {
            try {
                var certificate = X509Certificate2.CreateFromPem(entity.CertificatePem, entity.PrivateKeyPem);
                var material = new ManagedCertificateMaterial(entity.NodeId, entity.Domains.AsReadOnly(),
                    entity.CertificatePem, entity.PrivateKeyPem, entity.NotAfter);
                loadedCertificates.Add(certificate);
                foreach (var domain in entity.Domains) {
                    byDomain[domain] = certificate;
                    materialsByDomain[domain] = material;
                }

                byNode[entity.NodeId] = material;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to load managed certificate for node {NodeId}", entity.NodeId);
            }
        }

        lock (_lock) {
            _certificates.AddRange(loadedCertificates);
            _certificatesByDomain = byDomain;
            _materialsByDomain = materialsByDomain;
            _materialsByNode = byNode;
        }
    }

    private static string GetId(Guid nodeId) {
        return $"ManagedCertificates/{nodeId:D}";
    }
}