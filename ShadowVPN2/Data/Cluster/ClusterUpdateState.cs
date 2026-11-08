using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Cluster;
public sealed class ClusterUpdateState {
    public Guid Id { get; set; }
    public required string ImageDigest { get; set; }
    public string? DesiredImageDigest { get; set; }
    public required List<ClusterUpdateNodeState> Nodes { get; set; }
    public int CurrentNodeIndex { get; set; }
    public ClusterUpdateStatus Status { get; set; } = ClusterUpdateStatus.Preparing;
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}