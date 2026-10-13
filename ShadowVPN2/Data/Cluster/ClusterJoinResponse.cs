using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Cluster;

public class ClusterJoinResponse {
    public required List<NodeCertificateTrust> PeerCertificates { get; set; }
    public required List<AwgPeerInfo> AwgPeers { get; set; }
    public required AwgGlobalSettings AwgSettings { get; set; }
    public required int NodeNumber { get; set; }
}