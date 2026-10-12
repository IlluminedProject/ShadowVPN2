using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Nodes;

public sealed class DomainCheckResponse {
    public required DomainValidationState State { get; init; }
    public required HostName? Domain { get; init; }
    public required IReadOnlyList<string> ResolvedAddresses { get; init; }
    public required DateTimeOffset CheckedAt { get; init; }
    public string? Error { get; init; }
}