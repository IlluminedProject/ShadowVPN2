using ShadowVPN2.Entities.Base;

namespace ShadowVPN2.Entities;

public sealed class EntityNodeRuntimeState : IEntityId {
    public required Guid NodeId { get; init; }
    public required string InstanceId { get; init; }
    public DateTimeOffset HeartbeatAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public NodeRuntimeMetadata Metadata { get; set; } = new();
    public Dictionary<string, NodeCheckResult> Checks { get; set; } = [];
    public DomainCheckResponse? DomainDns { get; set; }
    public required string Id { get; init; }
}