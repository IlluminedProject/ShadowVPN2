using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Nodes;

public sealed class NodeCertificateDomainsResponse {
    public required Guid NodeId { get; init; }
    public required IReadOnlyList<HostName> Domains { get; init; }
}