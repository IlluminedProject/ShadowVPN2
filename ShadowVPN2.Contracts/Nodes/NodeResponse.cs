using System.Collections.ObjectModel;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Nodes;

public sealed class NodeResponse {
    public required string Id { get; set; }
    public required Guid NodeId { get; set; }
    public required string Name { get; set; }
    public HostName? Domain { get; set; }
    public string? PublicIpv4 { get; set; }
    public string? PublicIpv6 { get; set; }
    public IReadOnlyList<string> InternalAddresses { get; set; } = [];
    public DateTimeOffset? NetworkCheckedAt { get; set; }
    public DateTimeOffset? RuntimeHeartbeatAt { get; set; }
    public DateTimeOffset? RuntimeStartedAt { get; set; }
    public NodeRuntimeMetadata? RuntimeMetadata { get; set; }

    public IReadOnlyDictionary<string, NodeCheckResult> RuntimeChecks { get; set; } =
        ReadOnlyDictionary<string, NodeCheckResult>.Empty;

    public NodeRuntimeStatus RuntimeStatus { get; set; }
    public DomainCheckResponse? DomainStatus { get; set; }
    public int Number { get; set; }
    public string? AwgPublicKey { get; set; }
    public string AwgMeshIp { get; set; } = string.Empty;
    public bool IsPending { get; set; }
}