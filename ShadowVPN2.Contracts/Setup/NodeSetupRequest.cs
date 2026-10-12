using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Setup;

public sealed class NodeSetupRequest {
    public string NodeName { get; set; } = string.Empty;
    public HostName? Domain { get; set; }
}