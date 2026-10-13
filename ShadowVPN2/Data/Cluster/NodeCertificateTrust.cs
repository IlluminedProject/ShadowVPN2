namespace ShadowVPN2.Data.Cluster;

public sealed record NodeCertificateTrust(Guid NodeId, string MeshIp, string CertificatePem);