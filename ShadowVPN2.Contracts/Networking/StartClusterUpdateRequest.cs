namespace ShadowVPN2.Contracts.Networking;

public sealed class StartClusterUpdateRequest {
    public required string ImageDigest { get; set; }
    public required bool BackwardCompatible { get; set; }
}