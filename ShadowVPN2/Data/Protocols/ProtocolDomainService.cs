using System.Net;
using Raven.Client.Documents;
using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Protocols;

public sealed class ProtocolDomainService(
    IDocumentStore documentStore,
    GlobalConfigurationService globalConfigurationService,
    NodeNetworkService nodeNetworkService,
    DomainValidationService domainValidationService) {
    public async Task<IReadOnlyList<ProtocolDomainRouteResponse>> GetRoutesAsync(
        CancellationToken cancellationToken = default) {
        var globalConfiguration = await globalConfigurationService.GetAsync(cancellationToken);
        using var session = documentStore.OpenAsyncSession();
        var nodes = await session.Query<EntityClusterNode>().ToListAsync(cancellationToken);
        var statuses = await nodeNetworkService.GetStatusesAsync(nodes.Select(node => node.NodeId), cancellationToken);
        var addressesByNode = nodes.ToDictionary(
            node => node,
            node => GetNodeAddresses(statuses.GetValueOrDefault(node.NodeId)));

        var routes = new List<ProtocolDomainRouteResponse>(globalConfiguration.Protocols.Count);
        for (var index = 0; index < globalConfiguration.Protocols.Count; index++) {
            var protocol = globalConfiguration.Protocols[index];
            var domain = protocol.MainDomain ?? globalConfiguration.MainDomain;
            routes.Add(await ResolveRouteAsync(index, protocol.Protocol, protocol.Enabled, domain, addressesByNode,
                cancellationToken));
        }

        return routes.AsReadOnly();
    }

    public async Task<NodeCertificateDomainsResponse> GetCertificateDomainsAsync(Guid nodeId,
        CancellationToken cancellationToken = default) {
        using var session = documentStore.OpenAsyncSession();
        var node = await session.Query<EntityClusterNode>()
                       .FirstOrDefaultAsync(candidate => candidate.NodeId == nodeId, cancellationToken)
                   ?? throw new KeyNotFoundException("Node not found");
        var status = await nodeNetworkService.GetStatusAsync(nodeId, cancellationToken);
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var nodeDomainStatus = await domainValidationService.CheckAsync(node.Domain, status?.PublicIpv4,
            status?.PublicIpv6, cancellationToken);
        if (nodeDomainStatus.State == DomainValidationState.Valid && nodeDomainStatus.Domain != null)
            domains.Add(nodeDomainStatus.Domain);

        foreach (var route in await GetRoutesAsync(cancellationToken))
            if (route.Enabled && route.State == ProtocolDomainRouteState.Valid && route.NodeId == nodeId &&
                route.Domain != null)
                domains.Add(route.Domain);

        return new NodeCertificateDomainsResponse {
            NodeId = nodeId,
            Domains = domains.OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly()
        };
    }

    private async Task<ProtocolDomainRouteResponse> ResolveRouteAsync(
        int protocolIndex,
        string protocol,
        bool enabled,
        string? domain,
        IReadOnlyDictionary<EntityClusterNode, HashSet<IPAddress>> addressesByNode,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(domain))
            return Create(protocolIndex, protocol, enabled, null, [], [], ProtocolDomainRouteState.NoDomain);

        DomainResolutionResponse resolution;
        try {
            domain = domainValidationService.Normalize(domain)!;
            resolution = await domainValidationService.ResolveAsync(domain, cancellationToken);
        }
        catch (ArgumentException ex) {
            return Create(protocolIndex, protocol, enabled, domain, [], [], ProtocolDomainRouteState.LookupFailed,
                error: ex.Message);
        }

        if (resolution.Error != null)
            return Create(protocolIndex, protocol, enabled, domain, [], [], ProtocolDomainRouteState.LookupFailed,
                error: resolution.Error);
        if (resolution.Addresses.Count == 0)
            return Create(protocolIndex, protocol, enabled, domain, [], [], ProtocolDomainRouteState.NoRecords);

        var resolved = resolution.Addresses.Select(IPAddress.Parse).ToHashSet();
        var matchingNodes = addressesByNode
            .Where(pair => pair.Value.Overlaps(resolved))
            .Select(pair => pair.Key)
            .ToList();
        var knownAddresses = matchingNodes.SelectMany(node => addressesByNode[node]).ToHashSet();
        var unmatched = resolved.Where(address => !knownAddresses.Contains(address))
            .Select(address => address.ToString())
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matchingNodes.Count == 0)
            return Create(protocolIndex, protocol, enabled, domain, resolution.Addresses, unmatched,
                ProtocolDomainRouteState.UnknownNode);
        if (matchingNodes.Count > 1)
            return Create(protocolIndex, protocol, enabled, domain, resolution.Addresses, unmatched,
                ProtocolDomainRouteState.MultipleNodes);

        var node = matchingNodes[0];
        return Create(protocolIndex, protocol, enabled, domain, resolution.Addresses, unmatched,
            unmatched.Count == 0 ? ProtocolDomainRouteState.Valid : ProtocolDomainRouteState.PartialMatch,
            node.NodeId, node.Name);
    }

    private static HashSet<IPAddress> GetNodeAddresses(EntityNodeNetworkStatus? status) {
        var addresses = new HashSet<IPAddress>();
        if (IPAddress.TryParse(status?.PublicIpv4, out var ipv4)) addresses.Add(ipv4);
        if (IPAddress.TryParse(status?.PublicIpv6, out var ipv6)) addresses.Add(ipv6);
        return addresses;
    }

    private static ProtocolDomainRouteResponse Create(int protocolIndex, string protocol, bool enabled, string? domain,
        IReadOnlyList<string> resolvedAddresses, IReadOnlyList<string> unmatchedAddresses,
        ProtocolDomainRouteState state, Guid? nodeId = null, string? nodeName = null, string? error = null) {
        return new ProtocolDomainRouteResponse {
            ProtocolIndex = protocolIndex,
            Protocol = protocol,
            Enabled = enabled,
            Domain = domain,
            ResolvedAddresses = resolvedAddresses,
            UnmatchedAddresses = unmatchedAddresses,
            State = state,
            NodeId = nodeId,
            NodeName = nodeName,
            Error = error
        };
    }
}