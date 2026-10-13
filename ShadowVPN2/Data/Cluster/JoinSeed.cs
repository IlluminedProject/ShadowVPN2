using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Cluster;

public sealed record JoinSeed(HostAddress Address, string CertificatePem);