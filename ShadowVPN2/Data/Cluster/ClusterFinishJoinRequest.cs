namespace ShadowVPN2.Data.Cluster;

public record ClusterFinishJoinRequest(Guid Secret);

public record ClusterCompleteJoinRequest(Guid Secret, Guid NodeId, string CertificatePem);