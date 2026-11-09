namespace ShadowVPN2.Contracts.Nodes;

public sealed record NodeCheckResult {
    public NodeCheckStatus Status { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
    public DateTimeOffset ValidUntil { get; init; }
    public string? Message { get; init; }
}