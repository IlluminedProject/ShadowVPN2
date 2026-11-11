using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Client.ServerWide.Commands;
using Raven.Client.ServerWide.Operations;
using Sparrow.Json;

namespace ShadowVPN2.Infrastructure.RavenDb;

/// <summary>
///     Client for RavenDB cluster administrative HTTP operations not covered by official client SDK.
/// </summary>
public sealed class RavenDbClusterAdminClient(
    IDocumentStore documentStore,
    ILogger<RavenDbClusterAdminClient> logger) {
    /// <summary>
    ///     Adds a server node to the RavenDB cluster topology.
    /// </summary>
    /// <param name="nodeAddress">Full URL of the node to add (e.g., https://100.64.0.11:8888)</param>
    /// <param name="nodeTag">Cluster node tag (e.g., NAA, NAB)</param>
    /// <param name="clientCertificate">Client certificate for authentication</param>
    /// <param name="peerCertificates">Trusted peer certificates for server validation</param>
    /// <param name="expectedThumbprint">Expected thumbprint of the joining node</param>
    /// <param name="certificateMatcher">Function to validate server certificate against peer pins</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task AddNodeToClusterAsync(
        string nodeAddress,
        string nodeTag,
        X509Certificate2 clientCertificate,
        X509Certificate2[] peerCertificates,
        string expectedThumbprint,
        Func<X509Certificate2, X509Certificate2, bool> certificateMatcher,
        CancellationToken cancellationToken = default) {
        var normalizedNodeAddress = nodeAddress.TrimEnd('/');
        var topology = await documentStore.Maintenance.Server.SendAsync(
            new GetClusterTopologyOperation(), cancellationToken);
        var existingNode = topology.Topology.Members.FirstOrDefault(member =>
            string.Equals(member.Value.TrimEnd('/'), normalizedNodeAddress, StringComparison.OrdinalIgnoreCase));
        try {
            if (!string.IsNullOrEmpty(existingNode.Key) &&
                !string.Equals(existingNode.Key, nodeTag, StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    $"RavenDB URL {normalizedNodeAddress} is already assigned to node {existingNode.Key}; " +
                    $"refusing to replace an existing cluster member with {nodeTag}");
            }

            if (string.Equals(existingNode.Key, nodeTag, StringComparison.Ordinal)) {
                logger.LogInformation("Node {NodeTag} already exists in RavenDB cluster topology", nodeTag);
                return;
            }

            logger.LogInformation("Adding node {NodeUrl} with tag {NodeTag} to RavenDB cluster", normalizedNodeAddress,
                nodeTag);

            using var httpClient = CreateAuthenticatedClient(clientCertificate, peerCertificates, certificateMatcher);
            var ravenUrl = documentStore.Urls.First();
            var requestUrl =
                $"{ravenUrl}/admin/cluster/node?url={Uri.EscapeDataString(nodeAddress.TrimEnd('/'))}" +
                $"&tag={Uri.EscapeDataString(nodeTag)}" +
                $"&expectedThumbprint={expectedThumbprint}";
            using var response = await httpClient.PutAsync(requestUrl, null, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Failed to add node to cluster: {response.StatusCode} {errorBody}");
            }

            await WaitForClusterMembershipAsync(nodeTag, normalizedNodeAddress, cancellationToken);
            logger.LogInformation("RavenDB cluster membership confirmed for node {NodeTag}", nodeTag);
        }
        finally {
            foreach (var certificate in peerCertificates)
                certificate.Dispose();
        }
    }

    private async Task WaitForClusterMembershipAsync(string nodeTag, string nodeAddress,
        CancellationToken cancellationToken) {
        for (var attempt = 0; attempt < 50; attempt++) {
            var topology = await documentStore.Maintenance.Server.SendAsync(
                new GetClusterTopologyOperation(), cancellationToken);
            if (topology.Topology.Members.TryGetValue(nodeTag, out var address) &&
                string.Equals(address.TrimEnd('/'), nodeAddress, StringComparison.OrdinalIgnoreCase))
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"RavenDB cluster membership for node {nodeTag} was not confirmed.");
    }

    /// <summary>
    ///     Removes a server node from the RavenDB cluster topology.
    /// </summary>
    /// <param name="nodeTag">Cluster node tag to remove</param>
    /// <param name="clientCertificate">Client certificate for authentication</param>
    /// <param name="peerCertificates">Trusted peer certificates for server validation</param>
    /// <param name="certificateMatcher">Function to validate server certificate against peer pins</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task RemoveNodeFromClusterAsync(
        string nodeTag,
        X509Certificate2 clientCertificate,
        X509Certificate2[] peerCertificates,
        Func<X509Certificate2, X509Certificate2, bool> certificateMatcher,
        CancellationToken cancellationToken = default) {
        logger.LogInformation("Removing node {NodeTag} from RavenDB cluster topology", nodeTag);

        try {
            using var httpClient = CreateAuthenticatedClient(clientCertificate, peerCertificates, certificateMatcher);
            var ravenUrl = documentStore.Urls.First();
            var requestUrl = $"{ravenUrl}/admin/cluster/node?nodeTag={Uri.EscapeDataString(nodeTag)}";

            using var response = await httpClient.DeleteAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Failed to remove node from cluster: {response.StatusCode} {errorBody}");
            }

            logger.LogInformation("Successfully removed node {NodeTag} from RavenDB cluster", nodeTag);
        }
        finally {
            foreach (var cert in peerCertificates)
                cert.Dispose();
        }
    }

    private static HttpClient CreateAuthenticatedClient(
        X509Certificate2 certificate,
        X509Certificate2[] peerPins,
        Func<X509Certificate2, X509Certificate2, bool> certificateMatcher) {
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificate);
        handler.ServerCertificateCustomValidationCallback = (request, remote, _, _) => {
            if (remote == null)
                return false;

            foreach (var pin in peerPins) {
                if (certificateMatcher(remote, pin) && pin.MatchesHostname(request.RequestUri!.Host, false, false))
                    return true;
            }

            return false;
        };

        return new HttpClient(handler);
    }
}

internal sealed class GetClusterTopologyOperation : IServerOperation<ClusterTopologyResponse> {
    public RavenCommand<ClusterTopologyResponse> GetCommand(DocumentConventions conventions,
        JsonOperationContext context) {
        return new GetClusterTopologyCommand();
    }
}