using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Data.Subscription.Models.Mihomo;
using Contract = ShadowVPN2.Contracts.Subscriptions;
using NaiveConnection = ShadowVPN2.Data.Subscription.Models.Connections.NaiveProxyConnectionInfo;

namespace ShadowVPN2.Data.Subscription.Exporters;

public sealed class NaiveProxySubscriptionProtocolExporter : SubscriptionProtocolExporter<NaiveConnection> {
    public override OutboundConfig? CreateSingBoxOutbound(NaiveConnection connection) {
        // Cronet's TLS options cannot preserve the server certificate pin or insecure mode.
        if (connection.Insecure || connection.PinSha256 is not null)
            return null;
        return new NaiveProxyOutboundConfig {
            Server = connection.ServerAddress.ToString(), ServerPort = connection.ServerPort,
            Username = connection.Username, Password = connection.Password, Quic = connection.Quic,
            UdpOverTcp = true,
            Tls = new ClientTlsConfig { Enabled = true, ServerName = connection.Sni.ToString() }
        };
    }

    public override Contract.ProtocolConnectionInfo ToContract(NaiveConnection connection) {
        return new Contract.NaiveProxyConnectionInfo {
            ServerAddress = connection.ServerAddress,
            ServerPort = connection.ServerPort,
            Quic = connection.Quic,
            Username = connection.Username,
            Password = connection.Password,
            Sni = connection.Sni,
            Insecure = connection.Insecure,
            PinSha256 = connection.PinSha256
        };
    }

    public override string CreateShareUrl(NaiveConnection connection, string clientName) {
        return Contract.NaiveProxyShareUrlBuilder.Build((Contract.NaiveProxyConnectionInfo)ToContract(connection),
            clientName);
    }

    public override MihomoProxy? CreateMihomoProxy(NaiveConnection connection, string endpointName) {
        if (connection.Quic)
            return null;
        return new MihomoProxy(endpointName + " · NaiveProxy", "naive") {
            Server = connection.ServerAddress.ToString(), Port = connection.ServerPort,
            Username = connection.Username, Password = connection.Password,
            Sni = connection.Sni.ToString(),
            SkipCertVerify = connection.Insecure && connection.PinSha256 is null,
            Fingerprint = connection.PinSha256
        };
    }
}