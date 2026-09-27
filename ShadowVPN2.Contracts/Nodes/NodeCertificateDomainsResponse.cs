namespace ShadowVPN2.Contracts.Nodes;

public sealed class NodeCertificateDomainsResponse {
    public required Guid NodeId { get; init; }
    public required IReadOnlyList<string> Domains { get; init; }
}