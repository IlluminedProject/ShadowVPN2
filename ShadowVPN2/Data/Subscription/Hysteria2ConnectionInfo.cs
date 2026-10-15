using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Subscription;

public class Hysteria2ConnectionInfo : ProtocolConnectionInfo {
    public required HostName ServerAddress { get; set; }
    public PortBinding PortBinding { get; set; }
    public required string Password { get; set; }
    public string? ObfsType { get; set; }
    public string? ObfsPassword { get; set; }
    public HostName? Sni { get; set; }
    public string? PinSha256 { get; set; }

    public string CreateShareUrl(string clientName) {
        var builder = new ProxyUriBuilder("hysteria2", ServerAddress, PortBinding.ListenPort)
            .WithCredentials(Password)
            .AddQuery("insecure", "1")
            .AddQuery("pinSHA256", PinSha256)
            .AddQueryIf(ObfsType is not null and not "none", "obfs", ObfsType)
            .AddQueryIf(ObfsType is not null and not "none", "obfs-password", ObfsPassword)
            .AddQuery("sni", Sni?.ToString())
            .AddQuery("mport", PortBinding.PortRangeEnd.HasValue ? PortBinding.ToString() : null)
            .AddQuery("name", clientName);

        return builder.Build();
    }

    public Hysteria2ConnectionInfo WithEndpoint(HostName serverAddress, int serverPort) {
        return new Hysteria2ConnectionInfo {
            ServerAddress = serverAddress,
            PortBinding = new PortBinding(serverPort),
            Password = Password,
            ObfsType = ObfsType,
            ObfsPassword = ObfsPassword,
            Sni = Sni,
            PinSha256 = PinSha256
        };
    }
}