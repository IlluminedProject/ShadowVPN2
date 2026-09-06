using ShadowVPN2.Entities.Base;

namespace ShadowVPN2.Entities;

public sealed class EntityNodeNetworkStatus : IEntityId {
    public required string Id { get; init; }
    public required Guid NodeId { get; set; }
    public string? PublicIpv4 { get; set; }
    public string? PublicIpv6 { get; set; }
    public List<string> InternalAddresses { get; set; } = new();
    public DateTimeOffset CheckedAt { get; set; }
    public string? LastError { get; set; }
}