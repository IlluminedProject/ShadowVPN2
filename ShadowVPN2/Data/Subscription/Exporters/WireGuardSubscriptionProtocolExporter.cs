using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Data.Subscription.Models.Connections;
using ShadowVPN2.Data.Subscription.Models.Mihomo;
using ShadowVPN2.Data.Subscription.Models.Xray;
using Contract = ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Data.Subscription.Exporters;

public sealed class WireGuardSubscriptionProtocolExporter : SubscriptionProtocolExporter<WireGuardConnectionInfo> {
    public override string CreateShareUrl(WireGuardConnectionInfo connection, string clientName) => connection.CreateShareUrl();

    public override EndpointConfig CreateSingBoxEndpoint(WireGuardConnectionInfo connection) {
        if (connection.IsAmneziaWg)
            return new AwgEndpointConfig {
                Mtu = connection.Mtu, Address = [LocalAddress(connection)], PrivateKey = connection.PrivateKey,
                Peers = [new WireGuardPeer {
                    Address = connection.ServerAddress.ToString(), Port = connection.ServerPort,
                    PublicKey = connection.ServerPublicKey, AllowedIps = ["0.0.0.0/0"]
                }],
                Jc = connection.Jc, Jmin = connection.Jmin, Jmax = connection.Jmax,
                S1 = connection.S1, S2 = connection.S2, S3 = connection.S3, S4 = connection.S4,
                H1 = connection.H1, H2 = connection.H2, H3 = connection.H3, H4 = connection.H4,
                I1 = connection.I1, I2 = connection.I2, I3 = connection.I3, I4 = connection.I4, I5 = connection.I5
            };
        return new WireGuardEndpointConfig {
            System = false, Name = "", Mtu = connection.Mtu,
            Address = [LocalAddress(connection)], PrivateKey = connection.PrivateKey,
            Peers = [new WireGuardPeer {
                Address = connection.ServerAddress.ToString(), Port = connection.ServerPort,
                PublicKey = connection.ServerPublicKey, AllowedIps = ["0.0.0.0/0"]
            }]
        };
    }

    public override XrayOutboundConfig? CreateXrayOutbound(WireGuardConnectionInfo connection) {
        if (connection.IsAmneziaWg)
            return null;
        return new XrayWireGuardOutboundConfig {
            Settings = new XrayWireGuardSettings {
                SecretKey = connection.PrivateKey, Address = [LocalAddress(connection)], Mtu = connection.Mtu,
                Peers = [new XrayWireGuardPeer {
                    Endpoint = connection.ServerAddress.ToAddress(connection.ServerPort).FormatWithPort(),
                    PublicKey = connection.ServerPublicKey, AllowedIPs = ["0.0.0.0/0"]
                }]
            }
        };
    }

    private static string LocalAddress(WireGuardConnectionInfo connection) =>
        connection.AssignedIp.Contains('/') ? connection.AssignedIp : connection.AssignedIp + "/32";

    public override Contract.ProtocolConnectionInfo ToContract(WireGuardConnectionInfo connection) {
        return new Contract.WireGuardConnectionInfo {
            ServerAddress = connection.ServerAddress,
            ServerPort = connection.ServerPort,
            PrivateKey = connection.PrivateKey,
            AssignedIp = connection.AssignedIp,
            ServerPublicKey = connection.ServerPublicKey,
            Mtu = connection.Mtu,
            IsAmneziaWg = connection.IsAmneziaWg,
            Jc = connection.Jc,
            Jmin = connection.Jmin,
            Jmax = connection.Jmax,
            S1 = connection.S1,
            S2 = connection.S2,
            S3 = connection.S3,
            S4 = connection.S4,
            H1 = connection.H1,
            H2 = connection.H2,
            H3 = connection.H3,
            H4 = connection.H4,
            I1 = connection.I1,
            I2 = connection.I2,
            I3 = connection.I3,
            I4 = connection.I4,
            I5 = connection.I5
        };
    }

    public override MihomoProxy? CreateMihomoProxy(WireGuardConnectionInfo connection, string endpointName) {
        if (connection.IsAmneziaWg)
            return null;
        return new MihomoProxy(endpointName + " · WireGuard", "wireguard") {
            Server = connection.ServerAddress.ToString(), Port = connection.ServerPort,
            Ip = connection.AssignedIp, PrivateKey = connection.PrivateKey,
            PublicKey = connection.ServerPublicKey, Mtu = connection.Mtu,
            AllowedIps = ["0.0.0.0/0"], Udp = true
        };
    }
}
