namespace ShadowVPN2.Data.Cluster;

public sealed class ClusterUpdateNodeState {
    public required string NodeTag { get; set; }
    public ClusterUpdateNodeStatus Status { get; set; }
    public string? Error { get; set; }
    public int ReconciliationFailures { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}