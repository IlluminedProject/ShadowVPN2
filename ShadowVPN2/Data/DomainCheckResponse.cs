namespace ShadowVPN2.Data;

public sealed class DomainCheckResponse {
    public required DomainValidationState State { get; init; }
    public required string? Domain { get; init; }
    public required IReadOnlyList<string> ResolvedAddresses { get; init; }
    public required DateTimeOffset CheckedAt { get; init; }
    public string? Error { get; init; }
}