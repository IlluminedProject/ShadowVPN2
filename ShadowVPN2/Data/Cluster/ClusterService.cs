using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure;
using ShadowVPN2.Infrastructure.Configurations;
using SessionOptions = Raven.Client.Documents.Session.SessionOptions;
using TransactionMode = Raven.Client.Documents.Session.TransactionMode;

namespace ShadowVPN2.Data.Cluster;

public class ClusterService(
    IDocumentStore documentStore,
    NodeService nodeService,
    GlobalConfigurationService globalConfigurationService,
    IOptions<LocalConfiguration> localConfiguration,
    ILogger<ClusterService> logger) {
    public async Task<string> GenerateJoinTokenAsync(string name, string? externalAddress) {
        var nodeId = Guid.NewGuid();
        var secret = Guid.NewGuid();

        using var session = documentStore.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });

        var node = new EntityClusterNode {
            Id = "EntityClusterNodes|",
            NodeId = nodeId,
            Name = name,
            Address = externalAddress ?? "",
            JoinSecret = secret
        };

        await session.StoreAsync(node);
        await session.SaveChangesAsync();

        var existingNodes = await nodeService.GetNodesAsync();
        var nodeAddresses = existingNodes
            .Where(n => !string.IsNullOrEmpty(n.Address) && !n.JoinSecret.HasValue)
            .Select(n => n.Address)
            .ToList();

        var rootCaPem = await File.ReadAllTextAsync(LocalConfiguration.CertificatePemPath.Value);

        var token = new JoinToken {
            NodeAddresses = nodeAddresses,
            Secret = secret,
            Name = name,
            NodeId = nodeId,
            RootCaCertPem = rootCaPem
        };

        var json = JsonSerializer.Serialize(token, DataUtils.DefaultSerializerOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public async Task<ClusterSignJoinResponse> ExchangeTokenAsync(ClusterSignJoinRequest request, string? remoteIp) {
        using var session = documentStore.OpenAsyncSession();
        var nodes = await session.Query<EntityClusterNode>().ToListAsync();
        var pendingNode = nodes.FirstOrDefault(n => n.JoinSecret == request.Secret);

        if (pendingNode == null)
            throw new KeyNotFoundException("Invalid join secret");

        // Sign the CSR
        var signingCert = X509CertificateLoader.LoadPkcs12FromFile(LocalConfiguration.CertificatePfxPath.Value, null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
        var signedCert = RavenDbCertificates.SignCertificateFromPem(request.CsrPem, signingCert);
        var signedCertPem = signedCert.ExportCertificatePem();

        var rootCaPem = await File.ReadAllTextAsync(LocalConfiguration.CertificatePemPath.Value);

        // Update the pending node
        var nodeAddress = request.NodeAddress ?? remoteIp ?? "";
        pendingNode.AwgPublicKey = request.AwgPublicKey;
        if (!string.IsNullOrEmpty(nodeAddress))
            pendingNode.Address = nodeAddress;

        var localNode = nodes.FirstOrDefault(n => n.NodeId == localConfiguration.Value.NodeId);
        if (localNode != null && !string.IsNullOrEmpty(localConfiguration.Value.AwgPrivateKey))
            localNode.AwgPublicKey = AwgKeyGenerator.GetPublicKey(localConfiguration.Value.AwgPrivateKey);

        await session.SaveChangesAsync();

        // Build AWG peer list
        var globalConfig = await globalConfigurationService.GetAsync();
        var awgSettings = globalConfig.AwgSettings;

        var peers = nodes
            .Where(n => n.NodeId != pendingNode.NodeId && n.AwgPublicKey != null && !string.IsNullOrEmpty(n.Address))
            .Select(n => new AwgPeerInfo(n.AwgPublicKey!, n.AwgMeshIp, n.Address))
            .ToList();

        return new ClusterSignJoinResponse {
            SignedCertPem = signedCertPem,
            RootCaCertPem = rootCaPem,
            AwgPeers = peers,
            AwgSettings = awgSettings,
            NodeNumber = pendingNode.Number
        };
    }

    public async Task FinishJoinAsync(ClusterFinishJoinRequest request) {
        using var session = documentStore.OpenAsyncSession();
        var nodes = await session.Query<EntityClusterNode>().ToListAsync();
        var pendingNode = nodes.FirstOrDefault(n => n.JoinSecret == request.Secret);

        if (pendingNode == null)
            throw new KeyNotFoundException("Invalid join secret");

        var nodeAddress = $"https://{pendingNode.AwgMeshIp}:8888";
        var nodeTag = $"{pendingNode.Name}-{pendingNode.Number}";
        await AddNodeToRavenDbClusterAsync(nodeAddress, nodeTag);
        pendingNode.JoinSecret = null;
        await session.SaveChangesAsync();
    }

    private async Task AddNodeToRavenDbClusterAsync(string nodeAddress, string nodeTag) {
        var nodeUrl = nodeAddress.TrimEnd('/');
        logger.LogInformation("Adding node {NodeUrl} to RavenDB cluster via REST API", nodeUrl);

        var cert = X509CertificateLoader.LoadPkcs12FromFile(LocalConfiguration.CertificatePfxPath.Value, null);
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(cert);

        // ReSharper disable once ShortLivedHttpClient
        using var httpClient = new HttpClient(handler);
        var ravenUrl = documentStore.Urls.First();
        var requestUrl = $"{ravenUrl}/admin/cluster/node?url={Uri.EscapeDataString(nodeUrl)}&tag={nodeTag}";

        var response = await httpClient.PutAsync(requestUrl, null);
        if (response.IsSuccessStatusCode) {
            logger.LogInformation("Node {NodeUrl} added to RavenDB cluster", nodeUrl);
            return;
        }

        logger.LogError("Failed to add node to cluster: {StatusCode} {Body}", response.StatusCode,
            await response.Content.ReadAsStringAsync());

        throw new Exception("Failed to add node to cluster. Check logs for more details.");
    }
}