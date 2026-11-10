using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data;

public sealed class NodeDomainDnsCheck(
    IDocumentStore documentStore,
    IOptions<LocalConfiguration> localConfiguration,
    NodeNetworkService nodeNetworkService,
    DomainValidationService domainValidationService) {
    public const string CheckId = "dns";
    private DateTimeOffset? _lastNetworkCheckedAt;
    private string? _lastNetworkError;

    private DomainCheckResponse? _lastSnapshot;

    public async Task<DomainCheckResponse?> CheckAsync(CancellationToken cancellationToken) {
        var nodeId = localConfiguration.Value.NodeId;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var node = await LoadNodeAsync(nodeId, cancellationToken);
            var network = await nodeNetworkService.GetStatusAsync(nodeId, cancellationToken);
            var previous = _lastSnapshot;
            var snapshot = previous is not null && previous.ValidUntil > DateTimeOffset.UtcNow &&
                           previous.ObserverNodeId == nodeId && previous.Domain == node.Domain &&
                           previous.ExpectedIpv4 == network?.PublicIpv4 &&
                           previous.ExpectedIpv6 == network?.PublicIpv6 &&
                           _lastNetworkCheckedAt == network?.CheckedAt &&
                           _lastNetworkError == network?.LastError
                ? previous
                : node.Domain is null
                    ? null
                    : await domainValidationService.CheckAsync(node.Domain, network?.PublicIpv4, network?.PublicIpv6,
                        cancellationToken, network?.LastError is null ? network?.CheckedAt : null);
            if (snapshot is not null && snapshot.State != DomainValidationState.NetworkStateStale &&
                (network is null || network.LastError is not null))
                snapshot = snapshot with { State = DomainValidationState.NetworkStateStale };

            var currentNode = await LoadNodeAsync(nodeId, cancellationToken);
            var currentNetwork = await nodeNetworkService.GetStatusAsync(nodeId, cancellationToken);
            if (node.Domain != currentNode.Domain || network?.PublicIpv4 != currentNetwork?.PublicIpv4 ||
                network?.PublicIpv6 != currentNetwork?.PublicIpv6 || network?.CheckedAt != currentNetwork?.CheckedAt ||
                network?.LastError != currentNetwork?.LastError)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            _lastSnapshot = snapshot is null || snapshot.ObserverNodeId == nodeId
                ? snapshot
                : snapshot with { ObserverNodeId = nodeId };
            _lastNetworkCheckedAt = network?.CheckedAt;
            _lastNetworkError = network?.LastError;
            return _lastSnapshot;
        }
    }

    private async Task<EntityClusterNode> LoadNodeAsync(Guid nodeId, CancellationToken cancellationToken) {
        using var session = documentStore.OpenAsyncSession();
        return await session.Query<EntityClusterNode>()
                   .Customize(query => query.WaitForNonStaleResults())
                   .FirstOrDefaultAsync(node => node.NodeId == nodeId, cancellationToken)
               ?? throw new KeyNotFoundException("Local node not found in database");
    }
}