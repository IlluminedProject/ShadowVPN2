using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Cluster;

public sealed class GenerateNodeJoinTokenRequest {
    public required string Name { get; set; }
    public HostName? Domain { get; set; }
}