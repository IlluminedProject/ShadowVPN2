using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Frontend.Api;

public static class SubscriptionLinkBuilder {
    public static string CreateWireGuardConfig(WireGuardConnectionInfo connection) {
        var config = $"""
                      [Interface]
                      PrivateKey = {connection.PrivateKey}
                      Address = {connection.AssignedIp}/32
                      MTU = {connection.Mtu}

                      [Peer]
                      PublicKey = {connection.ServerPublicKey}
                      Endpoint = {FormatHost(connection.ServerAddress)}:{connection.ServerPort}
                      AllowedIPs = 0.0.0.0/0
                      PersistentKeepalive = 25

                      """;
        if (!connection.IsAmneziaWg)
            return config;

        config += $"""
                   Jc = {connection.Jc}
                   Jmin = {connection.Jmin}
                   Jmax = {connection.Jmax}
                   S1 = {connection.S1}
                   S2 = {connection.S2}
                   S3 = {connection.S3}
                   S4 = {connection.S4}
                   H1 = {connection.H1}
                   H2 = {connection.H2}
                   H3 = {connection.H3}
                   H4 = {connection.H4}

                   """;
        return config + Optional("I1", connection.I1) + Optional("I2", connection.I2) +
               Optional("I3", connection.I3) + Optional("I4", connection.I4) + Optional("I5", connection.I5);
    }

    public static string CreateWireGuardShareUrl(WireGuardConnectionInfo connection) {
        return $"wireguard://{Convert.ToBase64String(Encoding.UTF8.GetBytes(CreateWireGuardConfig(connection)))}";
    }

    public static string CreateHysteria2ShareUrl(Hysteria2ConnectionInfo connection, string clientName) {
        var builder = new StringBuilder("hysteria2://")
            .Append(Uri.EscapeDataString(connection.Password))
            .Append('@')
            .Append(FormatHost(connection.ServerAddress))
            .Append(':')
            .Append(connection.ServerPort)
            .Append("/?insecure=1");
        AddQuery(builder, "pinSHA256", connection.PinSha256);
        if (connection.ObfsType is not null and not "none") {
            AddQuery(builder, "obfs", connection.ObfsType);
            AddQuery(builder, "obfs-password", connection.ObfsPassword);
        }

        AddQuery(builder, "sni", connection.Sni);
        AddQuery(builder, "name", clientName);
        return builder.ToString();
    }

    public static string CreateNaiveProxyShareUrl(NaiveProxyConnectionInfo connection, string clientName) {
        var builder = new StringBuilder("naive+https://")
            .Append(Uri.EscapeDataString(connection.Username))
            .Append(':')
            .Append(Uri.EscapeDataString(connection.Password))
            .Append('@')
            .Append(FormatHost(connection.ServerAddress))
            .Append(':')
            .Append(connection.ServerPort)
            .Append("/?");
        AddQuery(builder, "sni", connection.Sni);
        AddQuery(builder, "insecure", connection.Insecure ? "1" : null);
        AddQuery(builder, "pinSHA256", connection.PinSha256);
        AddQuery(builder, "name", clientName);
        return builder.ToString();
    }

    public static string CreateFreeTurnShareUrl(FreeTurnConnectionInfo transport, string clientName,
        string? wireGuardConfig) {
        var payload = new Dictionary<string, object?> {
            ["v"] = 1,
            ["provider"] = "vk",
            ["peer"] = transport.Peer
        };
        var turnTransport = string.Equals(transport.TurnTransport, "Udp", StringComparison.OrdinalIgnoreCase)
            ? "udp"
            : null;
        if (turnTransport is not null)
            payload["transport"] = turnTransport;
        if (transport.Mode == 1)
            payload["mode"] = "tcp";
        AddPayloadValue(payload, "obf", GetObfuscationName(transport.ObfuscationProfile));
        AddPayloadValue(payload, "key", transport.ObfuscationKey);
        payload["n"] = transport.Streams;
        payload["cid"] = transport.ClientId;
        AddPayloadValue(payload, "name", clientName);
        AddPayloadValue(payload, "wg", wireGuardConfig);
        var json = JsonSerializer.Serialize(payload);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"freeturn://{encoded}";
    }

    public static ProtocolConnectionInfo CreateLocalConnection(ProtocolConnectionInfo connection) {
        return connection switch {
            WireGuardConnectionInfo wireGuard => CopyWireGuard(wireGuard, "127.0.0.1", 9000,
                Math.Min(wireGuard.Mtu, 1280)),
            Hysteria2ConnectionInfo hysteria2 => new Hysteria2ConnectionInfo {
                ServerAddress = "127.0.0.1",
                ServerPort = 9000,
                Password = hysteria2.Password,
                ObfsType = hysteria2.ObfsType,
                ObfsPassword = hysteria2.ObfsPassword,
                Sni = hysteria2.Sni,
                PinSha256 = hysteria2.PinSha256
            },
            NaiveProxyConnectionInfo naiveProxy => new NaiveProxyConnectionInfo {
                ServerAddress = "127.0.0.1",
                ServerPort = 9000,
                Username = naiveProxy.Username,
                Password = naiveProxy.Password,
                Sni = naiveProxy.Sni,
                Insecure = naiveProxy.Insecure,
                PinSha256 = naiveProxy.PinSha256
            },
            _ => throw new InvalidOperationException("Unsupported connection type")
        };
    }

    private static WireGuardConnectionInfo CopyWireGuard(WireGuardConnectionInfo source, string address, int port,
        int mtu) {
        return new WireGuardConnectionInfo {
            ServerAddress = address,
            ServerPort = port,
            PrivateKey = source.PrivateKey,
            AssignedIp = source.AssignedIp,
            ServerPublicKey = source.ServerPublicKey,
            Mtu = mtu,
            IsAmneziaWg = source.IsAmneziaWg,
            Jc = source.Jc,
            Jmin = source.Jmin,
            Jmax = source.Jmax,
            S1 = source.S1,
            S2 = source.S2,
            S3 = source.S3,
            S4 = source.S4,
            H1 = source.H1,
            H2 = source.H2,
            H3 = source.H3,
            H4 = source.H4,
            I1 = source.I1,
            I2 = source.I2,
            I3 = source.I3,
            I4 = source.I4,
            I5 = source.I5
        };
    }

    private static string FormatHost(string host) {
        return IPAddress.TryParse(host, out var address) &&
               address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{host}]"
            : host;
    }

    private static string Optional(string key, string? value) {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{key} = {value}\n";
    }

    private static void AddQuery(StringBuilder builder, string key, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            builder.Append(builder[^1] == '?' ? string.Empty : "&")
                .Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }
    }

    private static string? GetObfuscationName(string? profile) {
        return profile switch {
            "RtpOpus" => "rtpopus",
            "RtpOpus2" => "rtpopus2",
            "RtpOpus3" => "rtpopus3",
            _ => null
        };
    }

    private static void AddPayloadValue(IDictionary<string, object?> payload, string key, string? value) {
        if (!string.IsNullOrEmpty(value))
            payload[key] = value;
    }
}