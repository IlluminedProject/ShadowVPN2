namespace ShadowVPN2.Infrastructure.Configurations;

public sealed class UpdaterOptions {
    public string SocketPath { get; set; } = "/run/shadowvpn2/updater.sock";
}