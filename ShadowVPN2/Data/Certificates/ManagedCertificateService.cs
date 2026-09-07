using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data.Certificates;

public sealed class ManagedCertificateService(
    IDocumentStore documentStore,
    ILogger<ManagedCertificateService> logger) : IHostedService, IDisposable {
    private readonly Lock _lock = new();
    private readonly List<X509Certificate2> _certificates = [];

    private IReadOnlyDictionary<string, X509Certificate2> _certificatesByDomain =
        new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<Guid, ManagedCertificateMaterial> _materialsByNode =
        new Dictionary<Guid, ManagedCertificateMaterial>();

    private IDisposable? _subscription;

    public event Func<Task>? CertificatesChanged;

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

    public X509Certificate2? GetCertificate(string? serverName) {
        if (string.IsNullOrWhiteSpace(serverName)) return null;
        lock (_lock)
            return _certificatesByDomain.GetValueOrDefault(serverName);
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

    public async Task SaveAsync(Guid nodeId, IReadOnlyList<string> domains, string certificatePem,
        string privateKeyPem, CancellationToken cancellationToken = default) {
        using var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
        var normalizedDomains = domains.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase).ToList();
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

    public void Dispose() {
        _subscription?.Dispose();
        foreach (var certificate in _certificates) certificate.Dispose();
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
        var byDomain = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);
        var byNode = new Dictionary<Guid, ManagedCertificateMaterial>();
        var loadedCertificates = new List<X509Certificate2>();

        foreach (var entity in entities.Where(entity => !string.IsNullOrWhiteSpace(entity.CertificatePem) &&
                                                        !string.IsNullOrWhiteSpace(entity.PrivateKeyPem))) {
            try {
                var certificate = X509Certificate2.CreateFromPem(entity.CertificatePem, entity.PrivateKeyPem);
                loadedCertificates.Add(certificate);
                foreach (var domain in entity.Domains) byDomain[domain] = certificate;
                byNode[entity.NodeId] = new ManagedCertificateMaterial(entity.NodeId, entity.Domains.AsReadOnly(),
                    entity.CertificatePem, entity.PrivateKeyPem, entity.NotAfter);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to load managed certificate for node {NodeId}", entity.NodeId);
            }
        }

        lock (_lock) {
            _certificates.AddRange(loadedCertificates);
            _certificatesByDomain = byDomain;
            _materialsByNode = byNode;
        }
    }

    private static string GetId(Guid nodeId) => $"ManagedCertificates/{nodeId:D}";
}