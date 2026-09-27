using ShadowVPN2.Contracts.Nodes;
using ShadowVPN2.Contracts.Protocols;

namespace ShadowVPN2.Contracts.Application;

public interface INodeApplicationService {
    Task<IReadOnlyList<NodeResponse>> GetNodesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProtocolDomainRouteResponse>> GetProtocolDomainRoutesAsync(
        CancellationToken cancellationToken = default);

    Task<NodeResponse> UpdateDomainAsync(Guid nodeId, string? domain,
        CancellationToken cancellationToken = default);

    Task<DomainCheckResponse> CheckDomainAsync(Guid nodeId, CancellationToken cancellationToken = default);

    Task<NodeCertificateDomainsResponse> GetCertificateDomainsAsync(Guid nodeId,
        CancellationToken cancellationToken = default);
}