using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Nodes;

public sealed class UpdateNodeDomainRequest {
    public HostName? Domain { get; set; }
}