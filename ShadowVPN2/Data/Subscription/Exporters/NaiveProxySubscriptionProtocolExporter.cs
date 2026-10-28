using System.Text;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Data.Subscription.Models.Connections;
using ShadowVPN2.Data.Subscription.Models.Mihomo;
using Contract = ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Data.Subscription.Exporters;

public sealed class NaiveProxySubscriptionProtocolExporter : SubscriptionProtocolExporter<NaiveProxyConnectionInfo> {
    public override OutboundConfig? CreateSingBoxOutbound(NaiveProxyConnectionInfo connection) {
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

    public override Contract.ProtocolConnectionInfo ToContract(NaiveProxyConnectionInfo connection) {
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

    public override string CreateShareUrl(NaiveProxyConnectionInfo connection, string clientName) {
        var builder = new StringBuilder(connection.Quic ? "naive+quic://" : "naive+https://")
            .Append(Uri.EscapeDataString(connection.Username)).Append(':')
            .Append(Uri.EscapeDataString(connection.Password)).Append('@')
            .Append(connection.ServerAddress.ToAddress(connection.ServerPort).FormatWithPort()).Append("/?");
        AddQuery(builder, "sni", connection.Sni.ToString());
        AddQuery(builder, "insecure", connection.Insecure ? "1" : null);
        AddQuery(builder, "pinSHA256", connection.PinSha256);
        AddQuery(builder, "insecure-concurrency", "2");
        AddQuery(builder, "uot", "1");
        AddQuery(builder, "udp_over_tcp", "1");
        AddQuery(builder, "name", clientName);
        return builder.ToString().TrimEnd('&');
    }

    public override MihomoProxy? CreateMihomoProxy(NaiveProxyConnectionInfo connection, string endpointName) {
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

    private static void AddQuery(StringBuilder builder, string key, string? value) {
        if (!string.IsNullOrEmpty(value))
            builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value)).Append('&');
    }
}
