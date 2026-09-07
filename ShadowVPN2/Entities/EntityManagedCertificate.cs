using ShadowVPN2.Entities.Base;

namespace ShadowVPN2.Entities;

public sealed class EntityManagedCertificate : IEntityId {
    public required string Id { get; init; }
    public required Guid NodeId { get; set; }
    public List<string> Domains { get; set; } = [];
    public string CertificatePem { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}