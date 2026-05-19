using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Cluster;

public class ClusterSignJoinResponse
{
    public required string SignedCertPem { get; set; }
    public required string RootCaCertPem { get; set; }
    public required List<AwgPeerInfo> AwgPeers { get; set; }
    public required AwgGlobalSettings AwgSettings { get; set; }
    public required int NodeNumber { get; set; }
}