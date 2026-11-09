namespace ShadowVPN2.Updater;

public sealed class UpdaterOptions {
    public string ContainerName { get; set; } = "shadowvpn2";
    public string SelfContainerName { get; set; } = "shadowvpn2-updater";
    public string? ImageReference { get; set; }
    public string SocketPath { get; set; } = "/run/shadowvpn2/updater.sock";
}