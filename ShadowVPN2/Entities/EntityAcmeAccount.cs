using ShadowVPN2.Entities.Base;

namespace ShadowVPN2.Entities;

public sealed class EntityAcmeAccount : IEntityId {
    public required string Id { get; init; }
    public string AccountKeyPem { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsStaging { get; set; }
}