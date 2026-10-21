namespace ShadowVPN2.Data.Cluster;

public enum ClusterUpdateStatus {
    Running = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3,
    Preparing = 4
}