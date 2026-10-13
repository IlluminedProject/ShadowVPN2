using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.Cluster;

public sealed class PeerCertificateTrustService(
    IDocumentStore documentStore,
    IOptions<LocalConfiguration> localConfiguration,
    ILogger<PeerCertificateTrustService> logger) : BackgroundService {
    public static readonly string SnapshotPath = (LocalConfiguration.Path / "peer-certificates.json").Value;
    private static readonly SemaphoreSlim SnapshotLock = new(1, 1);

    private readonly Channel<bool> _updates = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    public static async Task<NodeCertificateTrust[]> LoadAsync(CancellationToken cancellationToken = default) {
        await using var stream = new FileStream(SnapshotPath, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<NodeCertificateTrust[]>(stream,
                   DataUtils.DefaultSerializerOptions, cancellationToken)
               ?? throw new InvalidDataException("Peer certificate snapshot is empty");
    }

    public static async Task SaveAsync(IEnumerable<NodeCertificateTrust> peers,
        CancellationToken cancellationToken = default) {
        await SnapshotLock.WaitAsync(cancellationToken);
        try {
            await WriteSnapshotAsync(peers, cancellationToken);
        }
        finally {
            SnapshotLock.Release();
        }
    }

    public static async Task UpsertAsync(NodeCertificateTrust peer, CancellationToken cancellationToken = default) {
        await SnapshotLock.WaitAsync(cancellationToken);
        try {
            var peers = File.Exists(SnapshotPath) ? await LoadAsync(cancellationToken) : [];
            await WriteSnapshotAsync(peers.Where(existing => existing.NodeId != peer.NodeId).Append(peer),
                cancellationToken);
        }
        finally {
            SnapshotLock.Release();
        }
    }

    private static async Task WriteSnapshotAsync(IEnumerable<NodeCertificateTrust> peers,
        CancellationToken cancellationToken) {
        var snapshot = peers.OrderBy(peer => peer.MeshIp, StringComparer.Ordinal).ToArray();
        var ids = new HashSet<Guid>();
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var peer in snapshot) {
            if (peer.NodeId == Guid.Empty || !ids.Add(peer.NodeId) || !addresses.Add(peer.MeshIp) ||
                !IPAddress.TryParse(peer.MeshIp, out var address) || address.ToString() != peer.MeshIp)
                throw new InvalidDataException("Peer certificate snapshot contains an invalid or duplicate node");
            using var certificate = RavenDbCertificates.LoadNodeCertificate(peer.CertificatePem, address);
        }

        var previous = File.Exists(SnapshotPath) ? await LoadAsync(cancellationToken) : [];
        SynchronizeRootStore(snapshot, previous);
        Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
        var temporaryPath = SnapshotPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough)) {
                await JsonSerializer.SerializeAsync(stream, snapshot, DataUtils.DefaultSerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }

            File.Move(temporaryPath, SnapshotPath, true);
        }
        finally {
            File.Delete(temporaryPath);
        }
    }

    public static Task RestoreRootStoreAsync(CancellationToken cancellationToken = default) {
        return RestoreRootStoreAsync([], cancellationToken);
    }

    public static async Task RestoreRootStoreAsync(IEnumerable<NodeCertificateTrust> temporaryPeers,
        CancellationToken cancellationToken = default) {
        await SnapshotLock.WaitAsync(cancellationToken);
        try {
            var peers = File.Exists(SnapshotPath)
                ? await LoadAsync(cancellationToken)
                : [];
            var previous = peers.Concat(temporaryPeers).ToArray();
            SynchronizeRootStore(peers, previous);
        }
        finally {
            SnapshotLock.Release();
        }
    }

    /// <summary>
    ///     Installs certificates for a join attempt without changing the durable snapshot.
    /// </summary>
    public static async Task InstallRootStoreAsync(IEnumerable<NodeCertificateTrust> peers,
        CancellationToken cancellationToken = default) {
        await SnapshotLock.WaitAsync(cancellationToken);
        try {
            var snapshot = peers.ToArray();
            foreach (var peer in snapshot) {
                cancellationToken.ThrowIfCancellationRequested();
                using var certificate = RavenDbCertificates.LoadNodeCertificate(peer.CertificatePem,
                    IPAddress.Parse(peer.MeshIp));
            }

            SynchronizeRootStore(snapshot, []);
        }
        finally {
            SnapshotLock.Release();
        }
    }

    private static void SynchronizeRootStore(NodeCertificateTrust[] peers, NodeCertificateTrust[] previous) {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var desired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var peer in peers) {
            using var certificate = RavenDbCertificates.LoadNodeCertificate(peer.CertificatePem,
                IPAddress.Parse(peer.MeshIp));
            desired.Add(certificate.Thumbprint);
            store.Add(certificate);
        }

        foreach (var peer in previous) {
            using var certificate = X509Certificate2.CreateFromPem(peer.CertificatePem);
            if (!desired.Contains(certificate.Thumbprint))
                store.Remove(certificate);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var changes = documentStore.Changes();
        var observable = changes.ForDocumentsInCollection<EntityClusterNode>();
        using var subscription = observable.Subscribe(new ActionObserver<DocumentChange>(
            _ => _updates.Writer.TryWrite(true),
            error => logger.LogError(error, "Peer certificate change subscription failed")));

        void OnConnectionStatusChanged(object? sender, EventArgs args) {
            _updates.Writer.TryWrite(true);
        }

        changes.ConnectionStatusChanged += OnConnectionStatusChanged;
        try {
            await changes.EnsureConnectedNow();
            await observable.EnsureSubscribedNow();
            await SynchronizeAsync(stoppingToken);
            await foreach (var _ in _updates.Reader.ReadAllAsync(stoppingToken))
                await SynchronizeAsync(stoppingToken);
        }
        finally {
            changes.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default) {
        await SnapshotLock.WaitAsync(cancellationToken);
        try {
            using var session = documentStore.OpenAsyncSession();
            var nodes = await session.Advanced.LoadStartingWithAsync<EntityClusterNode>(
                "EntityClusterNodes/", null, 0, int.MaxValue, null, null, cancellationToken);
            var peers = nodes.Where(node => node.CertificatePem != null)
                .Select(node => new NodeCertificateTrust(node.NodeId, node.AwgMeshIp, node.CertificatePem!));
            // Keep the local pin even before first setup creates its node document.
            var local = localConfiguration.Value;
            var localCertificate = await File.ReadAllTextAsync(LocalConfiguration.CertificatePemPath.Value,
                cancellationToken);
            await WriteSnapshotAsync(peers.Where(peer => peer.NodeId != local.NodeId).Append(
                    new NodeCertificateTrust(local.NodeId, $"100.64.0.{local.NodeNumber + 10}", localCertificate)),
                cancellationToken);
            logger.LogInformation("Updated peer certificate trust snapshot");
        }
        finally {
            SnapshotLock.Release();
        }
    }
}