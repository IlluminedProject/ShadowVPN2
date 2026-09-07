namespace ShadowVPN2.Data.Certificates;

public sealed record ManagedCertificateMaterial(
    Guid NodeId,
    IReadOnlyList<string> Domains,
    string CertificatePem,
    string PrivateKeyPem,
    DateTimeOffset NotAfter);