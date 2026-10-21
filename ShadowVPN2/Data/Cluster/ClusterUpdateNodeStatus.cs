namespace ShadowVPN2.Data.Cluster;

public enum ClusterUpdateNodeStatus {
    Pending = 0,
    Claimed = 1,
    Completed = 2,
    Failed = 3,
    Preparing = 4,
    Prepared = 5
}