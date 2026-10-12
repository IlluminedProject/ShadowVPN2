using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Cluster;

public sealed class ClusterSettingsResponse {
    public HostName? GlobalDomain { get; set; }
}