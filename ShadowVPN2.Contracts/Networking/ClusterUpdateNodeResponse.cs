namespace ShadowVPN2.Contracts.Networking;

public sealed class ClusterUpdateNodeResponse {
    public required string NodeTag { get; set; }
    public string? NodeName { get; set; }
    public string? NodeAddress { get; set; }
    public required string Status { get; set; }
    public string? Error { get; set; }
}