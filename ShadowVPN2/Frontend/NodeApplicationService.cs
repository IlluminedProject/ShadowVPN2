using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class NodeApplicationService(
    NodeService nodeService,
    ProtocolDomainService protocolDomainService,
    FrontendUserContext userContext) : INodeApplicationService {
    public async Task<IReadOnlyList<NodeResponse>> GetNodesAsync(CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Nodes.View, cancellationToken);
        return await nodeService.GetNodeResponsesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProtocolDomainRouteResponse>> GetProtocolDomainRoutesAsync(
        CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Nodes.View, cancellationToken);
        return await protocolDomainService.GetRoutesAsync(cancellationToken);
    }

    public async Task<NodeResponse> UpdateDomainAsync(Guid nodeId, string? domain,
        CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Nodes.Manage, cancellationToken);
        return await nodeService.UpdateDomainAsync(nodeId, domain, cancellationToken);
    }

    public async Task<DomainCheckResponse> CheckDomainAsync(Guid nodeId,
        CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Nodes.View, cancellationToken);
        return await nodeService.CheckDomainAsync(nodeId, cancellationToken);
    }

    public async Task<NodeCertificateDomainsResponse> GetCertificateDomainsAsync(Guid nodeId,
        CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Nodes.View, cancellationToken);
        return await protocolDomainService.GetCertificateDomainsAsync(nodeId, cancellationToken);
    }
}