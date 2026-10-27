namespace ShadowVPN2.Data.Cluster;

public sealed class ClusterUpdateDocument {
    public const string DocumentId = "ShadowVPN2/ClusterUpdate/State";

    public required ClusterUpdateState State { get; set; }
}