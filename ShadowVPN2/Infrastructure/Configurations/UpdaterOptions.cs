namespace ShadowVPN2.Infrastructure.Configurations;

public sealed class UpdaterOptions {
    public string SocketPath { get; set; } = "/run/shadowvpn2/updater.sock";
    public string? ImageReference { get; set; }
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(30);
}