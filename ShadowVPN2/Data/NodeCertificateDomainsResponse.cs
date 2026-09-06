namespace ShadowVPN2.Data;

public sealed class NodeCertificateDomainsResponse {
    public required Guid NodeId { get; init; }
    public required IReadOnlyList<string> Domains { get; init; }
}