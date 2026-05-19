namespace ShadowVPN2.Data.Cluster;

public class JoinToken
{
    public required List<string> NodeAddresses { get; set; }
    public required Guid Secret { get; set; }
    public required string Name { get; set; }
    public required Guid NodeId { get; set; }
    public required string RootCaCertPem { get; set; }
}