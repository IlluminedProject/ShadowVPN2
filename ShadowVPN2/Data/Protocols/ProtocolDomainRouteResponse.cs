namespace ShadowVPN2.Data.Protocols;

public sealed class ProtocolDomainRouteResponse {
    public required int ProtocolIndex { get; init; }
    public required string Protocol { get; init; }
    public required bool Enabled { get; init; }
    public required string? Domain { get; init; }
    public required IReadOnlyList<string> ResolvedAddresses { get; init; }
    public required IReadOnlyList<string> UnmatchedAddresses { get; init; }
    public required ProtocolDomainRouteState State { get; init; }
    public Guid? NodeId { get; init; }
    public string? NodeName { get; init; }
    public string? Error { get; init; }
}