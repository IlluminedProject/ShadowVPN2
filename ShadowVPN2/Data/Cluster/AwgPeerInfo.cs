using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Cluster;

public record AwgPeerInfo(string PublicKey, string MeshIp, HostAddress PublicAddress);