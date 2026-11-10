using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Nodes;

public sealed record DomainCheckResponse {
    public required DomainValidationState State { get; init; }
    public required HostName? Domain { get; init; }
    public required IReadOnlyList<string> ResolvedAddresses { get; init; }
    public required DateTimeOffset CheckedAt { get; init; }
    public string? ExpectedIpv4 { get; init; }
    public string? ExpectedIpv6 { get; init; }
    public DateTimeOffset? NetworkCheckedAt { get; init; }
    public DateTimeOffset ValidUntil { get; init; }
    public Guid? ObserverNodeId { get; init; }
    public DnsFamilyResult? A { get; init; }
    public DnsFamilyResult? Aaaa { get; init; }
    public bool IsComplete { get; init; }

    public bool? MatchesNode {
        get => ValidUntil <= DateTimeOffset.UtcNow || Domain is null
            ? null
            : State switch {
                DomainValidationState.Valid => true,
                DomainValidationState.Mismatch or DomainValidationState.PartialMatch or DomainValidationState.NoRecords
                    or DomainValidationState.NameNotFound => false,
                _ => null
            };
    }

    public string? Error { get; init; }
}