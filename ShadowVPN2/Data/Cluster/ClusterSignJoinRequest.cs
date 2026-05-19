namespace ShadowVPN2.Data.Cluster;

public class ClusterSignJoinRequest
{
    public required Guid Secret { get; set; }
    public required string CsrPem { get; set; }
    public required string AwgPublicKey { get; set; }
    public string? NodeAddress { get; set; }
}