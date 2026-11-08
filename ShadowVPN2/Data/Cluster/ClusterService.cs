using System.Net;
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
using ShadowVPN2.Infrastructure.RavenDb;
using SessionOptions = Raven.Client.Documents.Session.SessionOptions;
using TransactionMode = Raven.Client.Documents.Session.TransactionMode;

namespace ShadowVPN2.Data.Cluster;

public class ClusterService(
    IDocumentStore documentStore,
    NodeService nodeService,
    NodeNetworkService nodeNetworkService,
    GlobalConfigurationService globalConfigurationService,
    RavenDbClusterAdminClient ravenDbClusterAdminClient,
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
        var joinedNodes = existingNodes.Where(candidate => !candidate.JoinSecret.HasValue).ToArray();
        var statuses = await nodeNetworkService.GetStatusesAsync(
            joinedNodes.Select(node => node.NodeId), cancellationToken);
        var seeds = new List<JoinSeed>(joinedNodes.Length);
        foreach (var node in joinedNodes) {
            if (string.IsNullOrEmpty(node.CertificatePem))
                continue;
            statuses.TryGetValue(node.NodeId, out var status);
            var host = node.Domain ?? HostName.ParseOptional(status?.PublicIpv4 ?? status?.PublicIpv6);
            if (host.HasValue)
                seeds.Add(new JoinSeed(host.Value.ToAddress(status?.ManagementPort), node.CertificatePem));
        }

        if (seeds.Count == 0)
            throw new InvalidOperationException(
                "No joined node with a management address and trusted TLS certificate is available.");
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

    public async Task<ClusterJoinResponse> ExchangeTokenAsync(ClusterJoinRequest request, string? remoteIp,
        CancellationToken cancellationToken = default) {
        using var session = documentStore.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        var nodes = await session.Advanced.LoadStartingWithAsync<EntityClusterNode>("EntityClusterNodes/",
            pageSize: int.MaxValue, token: cancellationToken);
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
        await session.SaveChangesAsync(cancellationToken);
        await nodeNetworkService.RecordObservedPublicIpAsync(pendingNode.NodeId, remoteIp, cancellationToken);
        await PeerCertificateTrustService.UpsertAsync(new NodeCertificateTrust(pendingNode.NodeId,
            pendingNode.AwgMeshIp, pendingNode.CertificatePem), cancellationToken);
        await documentStore.Maintenance.Server.SendAsync(new PutClientCertificateOperation(
            $"{pendingNode.Name}-{pendingNode.Number}", certificate, new Dictionary<string, DatabaseAccess>(),
            SecurityClearance.ClusterNode), cancellationToken);

        var globalConfig = await globalConfigurationService.GetAsync(cancellationToken);
        var peerNodes = nodes.Where(node => node.NodeId != pendingNode.NodeId && !node.JoinSecret.HasValue
                                                                              && node.AwgPublicKey != null).ToArray();
        var statuses = await nodeNetworkService.GetStatusesAsync(peerNodes.Select(node => node.NodeId),
            cancellationToken);
        var peers = new List<AwgPeerInfo>(peerNodes.Length);
        foreach (var node in peerNodes) {
            statuses.TryGetValue(node.NodeId, out var status);
            var host = node.Domain ?? HostName.ParseOptional(status?.PublicIpv4 ?? status?.PublicIpv6);
            if (host.HasValue)
                peers.Add(new AwgPeerInfo(node.AwgPublicKey!, node.AwgMeshIp,
                    host.Value.ToAddress(status?.ManagementPort)));
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

    internal static string GetNodeTag(int nodeNumber) {
        return $"N{(char)('A' + nodeNumber / 26)}{(char)('A' + nodeNumber % 26)}";
    }

    private async Task AddNodeToRavenDbClusterAsync(string nodeAddress, string nodeTag) {
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            LocalConfiguration.CertificatePfxPath.Value, null);
        var peers = await PeerCertificateTrustService.LoadAsync();
        var pins = peers.Select(peer => X509Certificate2.CreateFromPem(peer.CertificatePem)).ToArray();
        var pendingIndex = Array.FindIndex(peers, peer => $"https://{peer.MeshIp}:8888" == nodeAddress);

        if (pendingIndex < 0) {
            foreach (var pin in pins)
                pin.Dispose();
            throw new InvalidOperationException(
                "The joining node certificate is missing from the local trust snapshot.");
        }

        await ravenDbClusterAdminClient.AddNodeToClusterAsync(nodeAddress, nodeTag, certificate, pins,
            pins[pendingIndex].Thumbprint, (remote, pin) => RavenDbCertificates.MatchesPin(remote, pin));
    }

    public async Task DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default) {
        if (nodeId == localConfiguration.Value.NodeId)
            throw new InvalidOperationException("Cannot delete the local node.");

        logger.LogInformation("Starting node deletion for {NodeId}", nodeId);

        EntityClusterNode? nodeDocument;
        using (var lookupSession = documentStore.OpenAsyncSession()) {
            nodeDocument = await lookupSession.Query<EntityClusterNode>()
                .FirstOrDefaultAsync(node => node.NodeId == nodeId, cancellationToken);
        }

        if (nodeDocument == null) {
            logger.LogWarning("Node {NodeId} not found", nodeId);
            throw new KeyNotFoundException("Node not found");
        }

        using var session = documentStore.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });
        var node = await session.LoadAsync<EntityClusterNode>(nodeDocument.Id, cancellationToken);
        if (node == null) {
            logger.LogWarning("Node {NodeId} disappeared during deletion", nodeId);
            throw new KeyNotFoundException("Node not found");
        }

        var isPending = node.JoinSecret.HasValue;
        var nodeTag = GetNodeTag(node.Number);

        if (!isPending) {
            // Removing the cluster member also removes its database replicas from RavenDB topology.
            logger.LogInformation(
                "Removing joined node {NodeId} (tag {NodeTag}, name {NodeName}) from cluster topology",
                nodeId, nodeTag, node.Name);

            try {
                // Remove server node from cluster topology
                logger.LogInformation("Removing server node {NodeTag} from RavenDB cluster", nodeTag);
                await RemoveNodeFromRavenDbClusterAsync(nodeTag, cancellationToken);
                logger.LogInformation("Server node {NodeTag} removed from RavenDB cluster", nodeTag);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to remove server node {NodeTag} from cluster", nodeTag);
                throw;
            }

            try {
                // Delete client certificate
                var certName = $"{node.Name}-{node.Number}";
                logger.LogInformation("Deleting client certificate {CertificateName}", certName);
                await documentStore.Maintenance.Server.SendAsync(
                    new DeleteCertificateOperation(certName), cancellationToken);
                logger.LogInformation("Client certificate {CertificateName} deleted", certName);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to delete client certificate for node {NodeTag}", nodeTag);
                throw;
            }
        }
        else {
            logger.LogInformation("Cancelling pending node {NodeId} (name {NodeName})", nodeId, node.Name);
        }

        // Delete node-scoped documents
        logger.LogInformation("Deleting node-scoped documents for {NodeId}", nodeId);
        session.Delete(node);

        var statusId = NodeNetworkService.GetStatusId(nodeId);
        var status = await session.LoadAsync<EntityNodeNetworkStatus>(statusId, cancellationToken);
        if (status != null) {
            logger.LogDebug("Deleting network status for node {NodeId}", nodeId);
            session.Delete(status);
        }

        var certificateId = ManagedCertificateService.GetId(nodeId);
        var certificate = await session.LoadAsync<EntityManagedCertificate>(certificateId, cancellationToken);
        if (certificate != null) {
            logger.LogDebug("Deleting managed certificate for node {NodeId}", nodeId);
            session.Delete(certificate);
        }

        await session.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Node {NodeId} (tag {NodeTag}, name {NodeName}) successfully deleted",
            nodeId, nodeTag, node.Name);
    }

    private async Task RemoveNodeFromRavenDbClusterAsync(string nodeTag, CancellationToken cancellationToken) {
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            LocalConfiguration.CertificatePfxPath.Value, null);
        var peers = await PeerCertificateTrustService.LoadAsync(cancellationToken);
        var pins = peers.Select(peer => X509Certificate2.CreateFromPem(peer.CertificatePem)).ToArray();

        await ravenDbClusterAdminClient.RemoveNodeFromClusterAsync(nodeTag, certificate, pins,
            (remote, pin) => RavenDbCertificates.MatchesPin(remote, pin),
            cancellationToken);
    }
}