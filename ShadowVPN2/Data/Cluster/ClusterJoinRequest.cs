namespace ShadowVPN2.Data.Cluster;

public class ClusterJoinRequest {
    public required Guid Secret { get; set; }
    public required string CertificatePem { get; set; }
    public required string AwgPublicKey { get; set; }
}