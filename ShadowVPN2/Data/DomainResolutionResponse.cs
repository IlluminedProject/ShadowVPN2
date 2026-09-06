namespace ShadowVPN2.Data;

public sealed class DomainResolutionResponse {
    public required string Domain { get; init; }
    public required IReadOnlyList<string> Addresses { get; init; }
    public string? Error { get; init; }
}