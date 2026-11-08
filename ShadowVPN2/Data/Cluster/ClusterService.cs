using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide.Operations.Certificates;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure;
using ShadowVPN2.Infrastructure.Configurations;
using SessionOptions = Raven.Client.Documents.Session.SessionOptions;
using TransactionMode = Raven.Client.Documents.Session.TransactionMode;

namespace ShadowVPN2.Data.Cluster;

public class ClusterService(
    IDocumentStore documentStore,
    NodeService nodeService,
    NodeNetworkService nodeNetworkService,
    GlobalConfigurationService globalConfigurationService,
    IOptions<LocalConfiguration> localConfiguration,
    ILogger<ClusterService> logger) {
    public async Task<string> GenerateJoinTokenAsync(string name, HostName? domain) {
        var existingNodes = await nodeService.GetNodesAsync();
        if (domain != null && existingNodes.Any(node => node.Domain == domain))
            throw new ArgumentException("Domain is already assigned to another node.");

        var seeds = await GetJoinSeedsAsync(existingNodes);
        var node = new EntityClusterNode {
            Id = "EntityClusterNodes|",
            NodeId = Guid.NewGuid(),
            Name = name,
            Domain = domain,
            JoinSecret = Guid.NewGuid()
        };
        using var session = documentStore.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        await session.StoreAsync(node);
        await session.SaveChangesAsync();

        return SerializeJoinToken(node, seeds);
    }

    public async Task<string> GetJoinTokenAsync(Guid nodeId, CancellationToken cancellationToken = default) {
        var nodes = await nodeService.GetNodesAsync();
        var node = nodes.FirstOrDefault(candidate => candidate.NodeId == nodeId)
                   ?? throw new KeyNotFoundException("Node not found");
        if (!node.JoinSecret.HasValue)
            throw new InvalidOperationException("The node join token is no longer available.");

        return SerializeJoinToken(node, await GetJoinSeedsAsync(nodes, cancellationToken));
    }

    private async Task<IReadOnlyList<JoinSeed>> GetJoinSeedsAsync(
        IReadOnlyList<EntityClusterNode> existingNodes, CancellationToken cancellationToken = default) {
        var seeds = new List<JoinSeed>();
        foreach (var existingNode in existingNodes.Where(candidate => !candidate.JoinSecret.HasValue)) {
            cancellationToken.ThrowIfCancellationRequested();
            var host = await nodeNetworkService.GetManagementHostAsync(existingNode);
            if (!host.HasValue || string.IsNullOrEmpty(existingNode.CertificatePem))
                continue;
            try {
                var leafPem = await GetManagementCertificateAsync(host.Value, existingNode.CertificatePem);
                seeds.Add(new JoinSeed(host.Value, leafPem));
            }
            catch (Exception exception) {
                logger.LogWarning(exception, "Cannot obtain a trusted management certificate for {Address}", host);
            }
        }

        if (seeds.Count == 0)
            throw new InvalidOperationException(
                "No reachable seed with a trusted management TLS certificate is available.");
        return seeds;
    }

    private static string SerializeJoinToken(EntityClusterNode node, IReadOnlyList<JoinSeed> seeds) {
        var token = new JoinToken {
            Seeds = seeds.ToList(),
            Secret = node.JoinSecret!.Value,
            Name = node.Name,
            NodeId = node.NodeId,
            NodeNumber = node.Number
        };
        var json = JsonSerializer.Serialize(token, DataUtils.DefaultSerializerOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static async Task<string> GetManagementCertificateAsync(HostAddress address, string nodeCertificatePem) {
        using var pin = X509Certificate2.CreateFromPem(nodeCertificatePem);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(address.Host.Value, address.Port ?? 443, timeout.Token);
        using var tls = new SslStream(tcp.GetStream(), false, (_, certificate, _, errors) => {
            if (certificate == null)
                return false;
            using var leaf = new X509Certificate2(certificate);
            return errors == SslPolicyErrors.None || RavenDbCertificates.MatchesPin(leaf, pin);
        });
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = address.Host.Value,
            CertificateRevocationCheckMode = X509RevocationMode.Online
        }, timeout.Token);
        using var remote = new X509Certificate2(tls.RemoteCertificate!);
        return remote.ExportCertificatePem();
    }

    public async Task<ClusterJoinResponse> ExchangeTokenAsync(ClusterJoinRequest request, string? remoteIp) {
        using var session = documentStore.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        var nodes = await session.Advanced.LoadStartingWithAsync<EntityClusterNode>("EntityClusterNodes/",
            pageSize: int.MaxValue);
        var pendingNode = nodes.FirstOrDefault(n => n.JoinSecret == request.Secret);
        if (pendingNode == null)
            throw new KeyNotFoundException("Invalid join secret");

        using var certificate = RavenDbCertificates.LoadNodeCertificate(request.CertificatePem,
            IPAddress.Parse(pendingNode.AwgMeshIp));
        // A pending secret identifies the join attempt, not a key pair. A retry may
        // legitimately replace temporary credentials after a process failure.
        pendingNode.CertificatePem = certificate.ExportCertificatePem();
        pendingNode.AwgPublicKey = request.AwgPublicKey;
        var localNode = nodes.FirstOrDefault(n => n.NodeId == localConfiguration.Value.NodeId);
        if (localNode != null && !string.IsNullOrEmpty(localConfiguration.Value.AwgPrivateKey))
            localNode.AwgPublicKey = AwgKeyGenerator.GetPublicKey(localConfiguration.Value.AwgPrivateKey);
        await session.SaveChangesAsync();
        await nodeNetworkService.RecordObservedPublicIpAsync(pendingNode.NodeId, remoteIp);

        // Both TLS directions must trust the pending node before cluster membership changes.
        await PeerCertificateTrustService.UpsertAsync(new NodeCertificateTrust(pendingNode.NodeId,
            pendingNode.AwgMeshIp, pendingNode.CertificatePem));
        await documentStore.Maintenance.Server.SendAsync(new PutClientCertificateOperation(
            $"{pendingNode.Name}-{pendingNode.Number}", certificate, new Dictionary<string, DatabaseAccess>(),
            SecurityClearance.ClusterNode));
        await PreparePeersAsync(nodes, new ClusterFinishJoinRequest(request.Secret));

        var globalConfig = await globalConfigurationService.GetAsync();
        var peers = new List<AwgPeerInfo>();
        foreach (var node in nodes.Where(node => node.NodeId != pendingNode.NodeId && !node.JoinSecret.HasValue
                     && node.AwgPublicKey != null)) {
            var host = await nodeNetworkService.GetManagementHostAsync(node);
            if (host.HasValue)
                peers.Add(new AwgPeerInfo(node.AwgPublicKey!, node.AwgMeshIp, host.Value));
        }

        return new ClusterJoinResponse {
            PeerCertificates = nodes.Where(node => node.CertificatePem != null)
                .Select(node => new NodeCertificateTrust(node.NodeId, node.AwgMeshIp, node.CertificatePem!)).ToList(),
            AwgPeers = peers,
            AwgSettings = globalConfig.AwgSettings,
            NodeNumber = pendingNode.Number
        };
    }

    public async Task FinishJoinAsync(ClusterFinishJoinRequest request) {
        using var session = documentStore.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        var nodes = await session.Advanced.LoadStartingWithAsync<EntityClusterNode>("EntityClusterNodes/",
            pageSize: int.MaxValue);
        var pendingNode = nodes.FirstOrDefault(n => n.JoinSecret == request.Secret);
        if (pendingNode == null)
            throw new KeyNotFoundException("Invalid join secret");
        if (pendingNode.CertificatePem == null || pendingNode.AwgPublicKey == null)
            throw new InvalidOperationException("The join certificate exchange has not completed.");
        await PeerCertificateTrustService.UpsertAsync(new NodeCertificateTrust(pendingNode.NodeId,
            pendingNode.AwgMeshIp, pendingNode.CertificatePem));
        await PreparePeersAsync(nodes, request);
        var nodeTag = GetNodeTag(pendingNode.Number);
        await AddNodeToRavenDbClusterAsync($"https://{pendingNode.AwgMeshIp}:8888", nodeTag);
        await documentStore.Maintenance.Server.SendAsync(new AddDatabaseNodeOperation(documentStore.Database, nodeTag));
        pendingNode.JoinMembershipCommitted = true;
        await session.SaveChangesAsync();
    }

    public async Task CompleteJoinAsync(ClusterCompleteJoinRequest request) {
        using var session = documentStore.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        var nodes = await session.Advanced.LoadStartingWithAsync<EntityClusterNode>("EntityClusterNodes/",
            pageSize: int.MaxValue);
        var pendingNode = nodes.FirstOrDefault(n => n.JoinSecret == request.Secret);
        if (pendingNode == null) {
            var completedNode = nodes.FirstOrDefault(n => n.NodeId == request.NodeId && n.JoinMembershipCommitted &&
                                                          n.CertificatePem != null);
            if (completedNode == null)
                throw new KeyNotFoundException("Invalid or already completed join secret");
            using var completedCertificate = RavenDbCertificates.LoadNodeCertificate(request.CertificatePem,
                IPAddress.Parse(completedNode.AwgMeshIp));
            if (!string.Equals(completedNode.CertificatePem, completedCertificate.ExportCertificatePem(),
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Join completion credentials do not match the completed node.");
            return;
        }

        using var certificate = RavenDbCertificates.LoadNodeCertificate(request.CertificatePem,
            IPAddress.Parse(pendingNode.AwgMeshIp));
        if (pendingNode.NodeId != request.NodeId || pendingNode.CertificatePem != certificate.ExportCertificatePem())
            throw new InvalidOperationException("Join completion credentials do not match the pending node.");
        pendingNode.JoinSecret = null;
        await session.SaveChangesAsync();
    }

    public async Task PrepareJoinAsync(ClusterFinishJoinRequest request) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true) {
            using var session = documentStore.OpenAsyncSession();
            var nodes = await session.Advanced.LoadStartingWithAsync<EntityClusterNode>("EntityClusterNodes/",
                pageSize: int.MaxValue, token: timeout.Token);
            var pending =
                nodes.FirstOrDefault(node => node.JoinSecret == request.Secret && node.CertificatePem != null);
            if (pending != null) {
                await PeerCertificateTrustService.UpsertAsync(new NodeCertificateTrust(pending.NodeId,
                    pending.AwgMeshIp, pending.CertificatePem!), timeout.Token);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }
    }

    private async Task PreparePeersAsync(IEnumerable<EntityClusterNode> nodes, ClusterFinishJoinRequest request) {
        foreach (var peer in nodes.Where(node => !node.JoinSecret.HasValue
                                                 && node.NodeId != localConfiguration.Value.NodeId)) {
            if (peer.CertificatePem == null)
                throw new InvalidOperationException($"Peer {peer.Name} has no persisted node certificate.");
            var address = await nodeNetworkService.GetManagementHostAsync(peer)
                          ?? throw new InvalidOperationException($"Peer {peer.Name} has no management address.");
            using var pin = X509Certificate2.CreateFromPem(peer.CertificatePem);
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None || RavenDbCertificates.MatchesPin(certificate, pin);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await client.PostAsJsonAsync($"https://{address}/api/cluster/prepare-join", request);
            response.EnsureSuccessStatusCode();
        }
    }

    internal static string GetNodeTag(int nodeNumber) {
        return $"N{(char)('A' + nodeNumber / 26)}{(char)('A' + nodeNumber % 26)}";
    }

    private async Task AddNodeToRavenDbClusterAsync(string nodeAddress, string nodeTag) {
        logger.LogInformation("Adding node {NodeUrl} to RavenDB cluster", nodeAddress);
        using var certificate =
            X509CertificateLoader.LoadPkcs12FromFile(LocalConfiguration.CertificatePfxPath.Value, null);
        using var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificate);
        var peers = await PeerCertificateTrustService.LoadAsync();
        var pins = peers.Select(peer => X509Certificate2.CreateFromPem(peer.CertificatePem)).ToArray();
        try {
            handler.ServerCertificateCustomValidationCallback = (request, remote, _, _) => remote != null
                && pins.Any(pin => RavenDbCertificates.MatchesPin(remote, pin)
                                   && pin.MatchesHostname(request.RequestUri!.Host, false,
                                       false));
            using var httpClient = new HttpClient(handler);
            var ravenUrl = documentStore.Urls.First();
            var pendingIndex = Array.FindIndex(peers, peer => $"https://{peer.MeshIp}:8888" == nodeAddress);
            if (pendingIndex < 0) {
                throw new InvalidOperationException(
                    "The joining node certificate is missing from the local trust snapshot.");
            }

            var requestUrl =
                $"{ravenUrl}/admin/cluster/node?url={Uri.EscapeDataString(nodeAddress.TrimEnd('/'))}&tag={Uri.EscapeDataString(nodeTag)}&expectedThumbprint={pins[pendingIndex].Thumbprint}";
            using var response = await httpClient.PutAsync(requestUrl, null);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException(
                    $"Failed to add node to cluster: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            }
        }
        finally {
            foreach (var pin in pins)
                pin.Dispose();
        }
    }

    public async Task DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default) {
        if (nodeId == localConfiguration.Value.NodeId)
            throw new InvalidOperationException("Cannot delete the local node.");

        using var session = documentStore.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        var nodes = await session.Advanced.LoadStartingWithAsync<EntityClusterNode>("EntityClusterNodes/",
            pageSize: int.MaxValue, token: cancellationToken);
        var node = nodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (node == null)
            throw new KeyNotFoundException("Node not found");

        var isPending = node.JoinSecret.HasValue;

        if (!isPending) {
            // Joined node: remove from RavenDB topology first
            var nodeTag = GetNodeTag(node.Number);
            logger.LogInformation("Removing joined node {NodeId} (tag {NodeTag}) from cluster", nodeId, nodeTag);

            // Remove database replica from this node
            await documentStore.Maintenance.Server.SendAsync(
                new DeleteDatabasesOperation(documentStore.Database, false, nodeTag),
                cancellationToken);

            // Remove server node from cluster topology
            await RemoveNodeFromRavenDbClusterAsync(nodeTag, cancellationToken);

            // Delete client certificate
            await documentStore.Maintenance.Server.SendAsync(
                new DeleteCertificateOperation($"{node.Name}-{node.Number}"), cancellationToken);
        }
        else {
            logger.LogInformation("Cancelling pending node {NodeId}", nodeId);
        }

        // Delete node-scoped documents
        session.Delete(node);

        var statusId = NodeNetworkService.GetStatusId(nodeId);
        var status = await session.LoadAsync<EntityNodeNetworkStatus>(statusId, cancellationToken);
        if (status != null)
            session.Delete(status);

        var certificateId = ManagedCertificateService.GetId(nodeId);
        var certificate = await session.LoadAsync<EntityManagedCertificate>(certificateId, cancellationToken);
        if (certificate != null)
            session.Delete(certificate);

        await session.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Node {NodeId} deleted", nodeId);
    }

    private async Task RemoveNodeFromRavenDbClusterAsync(string nodeTag, CancellationToken cancellationToken) {
        logger.LogInformation("Removing node {NodeTag} from RavenDB cluster topology", nodeTag);
        using var certificate =
            X509CertificateLoader.LoadPkcs12FromFile(LocalConfiguration.CertificatePfxPath.Value, null);
        using var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificate);
        var peers = await PeerCertificateTrustService.LoadAsync(cancellationToken);
        var pins = peers.Select(peer => X509Certificate2.CreateFromPem(peer.CertificatePem)).ToArray();
        try {
            handler.ServerCertificateCustomValidationCallback = (request, remote, _, _) => remote != null
                && pins.Any(pin => RavenDbCertificates.MatchesPin(remote, pin)
                                   && pin.MatchesHostname(request.RequestUri!.Host, false, false));
            using var httpClient = new HttpClient(handler);
            var ravenUrl = documentStore.Urls.First();
            var requestUrl = $"{ravenUrl}/admin/cluster/node?nodeTag={Uri.EscapeDataString(nodeTag)}";
            using var response = await httpClient.DeleteAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Failed to remove node from cluster: {response.StatusCode} {errorBody}");
            }
        }
        finally {
            foreach (var pin in pins)
                pin.Dispose();
        }
    }
}