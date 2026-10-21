namespace ShadowVPN2.Contracts.Networking;

public sealed class ClusterUpdateResponse {
    public required Guid Id { get; set; }
    public required string ImageDigest { get; set; }
    public required string Status { get; set; }
    public required IReadOnlyList<ClusterUpdateNodeResponse> Nodes { get; set; }
    public string? CurrentNodeTag { get; set; }
    public string? Error { get; set; }
}