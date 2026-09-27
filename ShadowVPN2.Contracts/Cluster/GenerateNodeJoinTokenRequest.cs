namespace ShadowVPN2.Contracts.Cluster;

public sealed class GenerateNodeJoinTokenRequest {
    public required string Name { get; set; }
    public string? Domain { get; set; }
}