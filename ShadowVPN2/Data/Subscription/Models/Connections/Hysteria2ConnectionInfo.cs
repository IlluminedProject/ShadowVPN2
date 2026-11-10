using ShadowVPN2.Contracts.Networking;
using Contract = ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Data.Subscription.Models.Connections;

public class Hysteria2ConnectionInfo : ProtocolConnectionInfo {
    public required HostName ServerAddress { get; set; }
    public PortBinding PortBinding { get; set; }
    public required string Password { get; set; }
    public string? ObfsType { get; set; }
    public string? ObfsPassword { get; set; }
    public HostName? Sni { get; set; }
    public string? PinSha256 { get; set; }
    public bool Insecure { get; set; }

    public string CreateShareUrl(string clientName) {
        return Contract.Hysteria2ShareUrlBuilder.Build(ToContract(), clientName);
    }

    public Contract.Hysteria2ConnectionInfo ToContract() {
        return new Contract.Hysteria2ConnectionInfo {
            ServerAddress = ServerAddress,
            PortBinding = PortBinding,
            Password = Password,
            ObfsType = ObfsType,
            ObfsPassword = ObfsPassword,
            Sni = Sni,
            PinSha256 = PinSha256,
            Insecure = Insecure
        };
    }

    public Hysteria2ConnectionInfo WithEndpoint(HostName serverAddress, int serverPort) {
        return new Hysteria2ConnectionInfo {
            ServerAddress = serverAddress,
            PortBinding = new PortBinding(serverPort),
            Password = Password,
            ObfsType = ObfsType,
            ObfsPassword = ObfsPassword,
            Sni = Sni,
            PinSha256 = PinSha256,
            Insecure = Insecure
        };
    }
}