using ShadowVPN2.Contracts.Cluster;

namespace ShadowVPN2.Frontend.Pages;

public sealed class ClusterSettingsForm {
    public ClusterSettingsResponse Cluster { get; set; } = new();
    public AwgClusterSettings Awg { get; set; } = new();
}