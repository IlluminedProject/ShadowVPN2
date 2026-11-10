namespace ShadowVPN2.Contracts.Nodes;

public sealed class DnsFamilyResult {
    public required IReadOnlyList<string> Addresses { get; init; }
    public required IReadOnlyList<string> CanonicalNames { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    public string? Error { get; init; }
    public bool NameNotFound { get; init; }
}