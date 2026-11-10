using System.Security.Cryptography;
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
    private readonly X509Certificate2 _bootstrapCertificate = CreateBootstrapCertificate();
    private readonly List<X509Certificate2> _certificates = [];
    private readonly Lock _lock = new();

    private IReadOnlyDictionary<HostName, X509Certificate2> _certificatesByDomain =
        new Dictionary<HostName, X509Certificate2>();

    private IReadOnlyDictionary<HostName, ManagedCertificateMaterial> _materialsByDomain =
        new Dictionary<HostName, ManagedCertificateMaterial>();

    private IReadOnlyDictionary<Guid, IReadOnlyList<ManagedCertificateMaterial>> _materialsByNode =
        new Dictionary<Guid, IReadOnlyList<ManagedCertificateMaterial>>();

    private IDisposable? _subscription;


    public void Dispose() {
        _subscription?.Dispose();
        _bootstrapCertificate.Dispose();
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

    public X509Certificate2 GetBootstrapCertificate() {
        return _bootstrapCertificate;
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
        return GetForNodeDomains(nodeId).FirstOrDefault();
    }

    public IReadOnlyList<ManagedCertificateMaterial> GetForNodeDomains(Guid nodeId) {
        lock (_lock)
            return _materialsByNode.GetValueOrDefault(nodeId) ?? [];
    }

    public X509Certificate2? GetDefaultCertificate(Guid nodeId) {
        lock (_lock) {
            var material = _materialsByNode.GetValueOrDefault(nodeId)?.FirstOrDefault();
            return material is null ? null : _certificatesByDomain.GetValueOrDefault(material.Domains[0]);
        }
    }

    public async Task<IReadOnlyList<EntityManagedCertificate>> GetEntitiesAsync(Guid nodeId,
        CancellationToken cancellationToken = default) {
        using var session = documentStore.OpenAsyncSession();
        return await session.Query<EntityManagedCertificate>().Where(entity => entity.NodeId == nodeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<EntityManagedCertificate?> GetEntityAsync(Guid nodeId,
        CancellationToken cancellationToken = default) {
        return (await GetEntitiesAsync(nodeId, cancellationToken))
            .OrderBy(entity => entity.Domain?.ToString(), StringComparer.Ordinal).FirstOrDefault();
    }

    public async Task SaveAsync(Guid nodeId, IReadOnlyList<HostName> domains, string certificatePem,
        string privateKeyPem, CancellationToken cancellationToken = default, int domainOrder = 0) {
        using var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
        var domain = domains.Distinct().Single();
        var dnsNames = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>()
            .SelectMany(extension => extension.EnumerateDnsNames()).ToArray();
        if (dnsNames.Length != 1 || !string.Equals(dnsNames[0], domain.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Managed certificates must have exactly one matching DNS SAN");
        var id = GetId(nodeId, domain);
        using var session = documentStore.OpenAsyncSession();
        var entity = await session.LoadAsync<EntityManagedCertificate>(id, cancellationToken)
                     ?? new EntityManagedCertificate { Id = id, NodeId = nodeId };
        entity.Domain = domain;
        entity.DomainOrder = domainOrder;
        entity.Domains = [domain];
        entity.CertificatePem = certificatePem;
        entity.PrivateKeyPem = privateKeyPem;
        entity.NotBefore = certificate.NotBefore.ToUniversalTime();
        entity.NotAfter = certificate.NotAfter.ToUniversalTime();
        entity.LastAttemptAt = DateTimeOffset.UtcNow;
        entity.LastError = null;
        await session.StoreAsync(entity, id, cancellationToken);
        var legacy = await session.LoadAsync<EntityManagedCertificate>(GetId(nodeId), cancellationToken);
        if (legacy is { Domain: null }) session.Delete(legacy);
        await session.SaveChangesAsync(cancellationToken);
        await ReloadAndNotifyAsync(cancellationToken);
    }

    public async Task<bool> TryReuseAsync(Guid nodeId, IReadOnlyList<HostName> domains,
        CancellationToken cancellationToken = default) {
        using var session = documentStore.OpenAsyncSession();
        var entities = await session.Query<EntityManagedCertificate>().ToListAsync(cancellationToken);
        var targets = entities.Where(entity => entity.NodeId == nodeId && entity.Domain is not null)
            .ToDictionary(entity => entity.Domain!.Value);
        var reused = false;
        foreach (var domain in domains.Distinct()) {
            if (targets.TryGetValue(domain, out var existing) && existing.CertificatePem.Length > 0) continue;
            var candidate = entities.Where(entity => entity.NodeId != nodeId && entity.Domain == domain &&
                                                     !string.IsNullOrWhiteSpace(entity.CertificatePem) &&
                                                     !string.IsNullOrWhiteSpace(entity.PrivateKeyPem))
                .FirstOrDefault(entity =>
                    IsSingleDomainCertificate(entity.CertificatePem, entity.PrivateKeyPem, domain));
            if (candidate is null) continue;
            var id = GetId(nodeId, domain);
            var target = existing ?? new EntityManagedCertificate { Id = id, NodeId = nodeId };
            target.Domain = domain;
            target.DomainOrder = Array.IndexOf(domains.Distinct().ToArray(), domain);
            target.Domains = [domain];
            target.CertificatePem = candidate.CertificatePem;
            target.PrivateKeyPem = candidate.PrivateKeyPem;
            target.NotBefore = candidate.NotBefore;
            target.NotAfter = candidate.NotAfter;
            target.LastAttemptAt = candidate.LastAttemptAt;
            target.LastError = null;
            await session.StoreAsync(target, id, cancellationToken);
            reused = true;
            session.Delete(candidate);
        }

        if (!reused) return false;
        var legacy = await session.LoadAsync<EntityManagedCertificate>(GetId(nodeId), cancellationToken);
        if (legacy is { Domain: null }) session.Delete(legacy);
        await session.SaveChangesAsync(cancellationToken);
        await ReloadAndNotifyAsync(cancellationToken);
        return true;
    }

    public async Task RecordErrorAsync(Guid nodeId, string error, CancellationToken cancellationToken = default) {
        using var session = documentStore.OpenAsyncSession();
        var entities = await session.Query<EntityManagedCertificate>().Where(entity => entity.NodeId == nodeId)
            .ToListAsync(cancellationToken);
        foreach (var entity in entities) {
            entity.LastAttemptAt = DateTimeOffset.UtcNow;
            entity.LastError = error;
            await session.StoreAsync(entity, entity.Id, cancellationToken);
        }

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
        var byNode = new Dictionary<Guid, List<ManagedCertificateMaterial>>();
        var loadedCertificates = new List<X509Certificate2>();
        foreach (var entity in entities.Where(entity => entity.Domain is not null &&
                                                        !string.IsNullOrWhiteSpace(entity.CertificatePem) &&
                                                        !string.IsNullOrWhiteSpace(entity.PrivateKeyPem))) {
            try {
                var certificate = X509Certificate2.CreateFromPem(entity.CertificatePem, entity.PrivateKeyPem);
                var domain = entity.Domain!.Value;
                var dnsNames = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>()
                    .SelectMany(extension => extension.EnumerateDnsNames()).ToArray();
                if (dnsNames.Length != 1 || !string.Equals(dnsNames[0], domain.ToString(),
                        StringComparison.OrdinalIgnoreCase)) {
                    certificate.Dispose();
                    continue;
                }

                var material = new ManagedCertificateMaterial(entity.NodeId, [domain], entity.CertificatePem,
                    entity.PrivateKeyPem, entity.NotAfter);
                loadedCertificates.Add(certificate);
                byDomain[domain] = certificate;
                materialsByDomain[domain] = material;
                if (!byNode.TryGetValue(entity.NodeId, out var nodeMaterials))
                    byNode[entity.NodeId] = nodeMaterials = [];
                nodeMaterials.Add(material);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to load managed certificate for node {NodeId}", entity.NodeId);
            }
        }

        lock (_lock) {
            _certificates.AddRange(loadedCertificates);
            _certificatesByDomain = byDomain;
            _materialsByDomain = materialsByDomain;
            _materialsByNode = byNode.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<ManagedCertificateMaterial>)pair.Value
                    .OrderBy(material => entities.First(entity => entity.NodeId == pair.Key &&
                                                                  entity.Domain == material.Domains[0]).DomainOrder)
                    .ThenBy(material => material.Domains[0].ToString(), StringComparer.Ordinal).ToArray());
        }
    }

    internal static string GetId(Guid nodeId) {
        return $"ManagedCertificates/{nodeId:D}";
    }

    internal static string GetId(Guid nodeId, HostName domain) {
        return $"ManagedCertificates/{nodeId:D}/{Uri.EscapeDataString(domain.ToString()!)}";
    }

    private static X509Certificate2 CreateBootstrapCertificate() {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=temporary.invalid", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("temporary.invalid");
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(12));
    }

    private static bool IsSingleDomainCertificate(string certificatePem, string privateKeyPem, HostName domain) {
        try {
            using var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
            var dnsNames = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>()
                .SelectMany(extension => extension.EnumerateDnsNames()).ToArray();
            return dnsNames.Length == 1 &&
                   string.Equals(dnsNames[0], domain.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException) {
            return false;
        }
    }
}