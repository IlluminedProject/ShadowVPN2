namespace ShadowVPN2.Contracts.Networking;

public sealed class CompleteClusterUpdateRequest {
    public required Guid UpdateId { get; set; }
    public required bool Succeeded { get; set; }
    public string? ImageDigest { get; set; }
    public string? Error { get; set; }
}