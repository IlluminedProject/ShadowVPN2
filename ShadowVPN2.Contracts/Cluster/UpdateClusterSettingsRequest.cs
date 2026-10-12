using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Cluster;

public sealed class UpdateClusterSettingsRequest {
    public HostName? GlobalDomain { get; set; }
}