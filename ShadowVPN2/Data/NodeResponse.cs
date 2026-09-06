namespace ShadowVPN2.Data;

public class NodeResponse {
    public required string Id { get; set; }
    public required Guid NodeId { get; set; }
    public required string Name { get; set; }
    public string? Domain { get; set; }
    public string? PublicIpv4 { get; set; }
    public string? PublicIpv6 { get; set; }
    public IReadOnlyList<string> InternalAddresses { get; set; } = [];
    public DateTimeOffset? NetworkCheckedAt { get; set; }
    public DomainCheckResponse? DomainStatus { get; set; }
    public int Number { get; set; }
    public string? AwgPublicKey { get; set; }
    public string AwgMeshIp { get; set; } = "";
    public bool IsPending { get; set; }
}