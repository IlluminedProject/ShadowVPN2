using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Certificates;

public sealed record ManagedCertificateMaterial(
    Guid NodeId,
    IReadOnlyList<HostName> Domains,
    string CertificatePem,
    string PrivateKeyPem,
    DateTimeOffset NotAfter);