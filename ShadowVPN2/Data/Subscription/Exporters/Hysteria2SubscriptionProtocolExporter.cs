using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Data.Subscription.Models.Connections;
using ShadowVPN2.Data.Subscription.Models.Mihomo;
using ShadowVPN2.Data.Subscription.Models.Xray;
using Contract = ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Data.Subscription.Exporters;

public sealed class Hysteria2SubscriptionProtocolExporter : SubscriptionProtocolExporter<Hysteria2ConnectionInfo> {
    public override string CreateShareUrl(Hysteria2ConnectionInfo connection, string clientName) => connection.CreateShareUrl(clientName);

    public override OutboundConfig CreateSingBoxOutbound(Hysteria2ConnectionInfo connection) => new Hysteria2OutboundConfig {
        Server = connection.ServerAddress.ToString(), Password = connection.Password,
        ServerPort = connection.PortBinding.PortRangeEnd.HasValue ? null : connection.PortBinding.ListenPort,
        ServerPorts = connection.PortBinding.PortRangeEnd is { } end
            ? [$"{connection.PortBinding.ListenPort}:{end}"] : null,
        Tls = new ClientTlsConfig {
            Enabled = true, ServerName = connection.Sni?.ToString(),
            CertificateSha256 = connection.PinSha256 is { } pin
                ? [Convert.ToBase64String(Convert.FromHexString(pin.Replace(":", "")))] : null
        },
        Obfs = connection.ObfsType is not null and not "none"
            ? new Hysteria2ObfsConfig { Type = connection.ObfsType, Password = connection.ObfsPassword! } : null
    };

    public override XrayOutboundConfig CreateXrayOutbound(Hysteria2ConnectionInfo connection) => new XrayHysteriaOutboundConfig {
        Settings = new XrayHysteriaSettings {
            Address = connection.ServerAddress.ToString(), Port = connection.PortBinding.ListenPort
        },
        StreamSettings = new XrayHysteriaStreamSettings {
            HysteriaSettings = new XrayHysteriaTransportSettings { Auth = connection.Password },
            TlsSettings = new XrayTlsSettings {
                ServerName = connection.Sni?.ToString(), Alpn = ["h3"], PinnedPeerCertSha256 = connection.PinSha256
            },
            FinalMask = connection.ObfsType is not null and not "none" ? new XrayFinalMaskConfig {
                Udp = [new XrayUdpMaskConfig {
                    Type = connection.ObfsType, Settings = new XrayUdpMaskSettings { Password = connection.ObfsPassword }
                }]
            } : null
        }
    };

    public override Contract.ProtocolConnectionInfo ToContract(Hysteria2ConnectionInfo connection) {
        return new Contract.Hysteria2ConnectionInfo {
            ServerAddress = connection.ServerAddress,
            PortBinding = connection.PortBinding,
            Password = connection.Password,
            ObfsType = connection.ObfsType,
            ObfsPassword = connection.ObfsPassword,
            Sni = connection.Sni,
            PinSha256 = connection.PinSha256
        };
    }

    public override MihomoProxy CreateMihomoProxy(Hysteria2ConnectionInfo connection, string endpointName) {
        return new MihomoProxy(endpointName + " · Hysteria2", "hysteria2") {
            Server = connection.ServerAddress.ToString(), Port = connection.PortBinding.ListenPort,
            Ports = connection.PortBinding.PortRangeEnd.HasValue ? connection.PortBinding.ToString() : null,
            Password = connection.Password, Sni = connection.Sni?.ToString(),
            SkipCertVerify = false, Fingerprint = connection.PinSha256,
            Obfs = connection.ObfsType is not null and not "none" ? connection.ObfsType : null,
            ObfsPassword = connection.ObfsType is not null and not "none" ? connection.ObfsPassword : null
        };
    }
}
