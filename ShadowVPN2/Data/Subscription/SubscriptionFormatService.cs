using System.Text;
using System.Text.Json;

namespace ShadowVPN2.Data.Subscription;

public sealed class SubscriptionFormatService {
    public string Render(SubscriptionResponse subscription, SubscriptionFormat format) {
        return format switch {
            SubscriptionFormat.Base64 => RenderBase64(subscription),
            SubscriptionFormat.Mihomo => RenderMihomo(subscription, false),
            SubscriptionFormat.MihomoProvider => RenderMihomo(subscription, true),
            SubscriptionFormat.FreeTurn => RenderFreeTurn(subscription),
            _ => throw new ArgumentException($"Unsupported subscription format: {format}", nameof(format))
        };
    }

    private static string RenderBase64(SubscriptionResponse subscription) {
        var links = subscription.Protocols
            .SelectMany(protocol => protocol.Endpoints.Select(endpoint =>
                (protocol, endpoint, connection: endpoint.Connection)))
            .SelectMany(item => item.connection switch {
                Hysteria2ConnectionInfo hysteria2 => [hysteria2.CreateShareUrl(subscription.ClientName)],
                NaiveProxyConnectionInfo naive => [CreateNaiveProxyShareUrl(naive, subscription.ClientName)],
                WireGuardConnectionInfo wireGuard => [wireGuard.CreateShareUrl()],
                _ => Array.Empty<string>()
            })
            .ToArray();

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', links)));
    }

    private static string RenderFreeTurn(SubscriptionResponse subscription) {
        var links = subscription.Protocols
            .SelectMany(protocol => protocol.Endpoints)
            .SelectMany(endpoint => endpoint.Transports.OfType<FreeTurnConnectionInfo>()
                .Select(transport => transport.CreateShareUrl(subscription.ClientName)))
            .ToArray();
        return string.Join('\n', links);
    }

    private static string RenderMihomo(SubscriptionResponse subscription, bool providerOnly) {
        var proxies = subscription.Protocols
            .SelectMany(protocol => protocol.Endpoints.Select(endpoint =>
                CreateProxy(endpoint)))
            .Where(proxy => proxy is not null)
            .Cast<MihomoProxy>()
            .ToArray();
        var builder = new StringBuilder("proxies:\n");
        foreach (var proxy in proxies)
            AppendProxy(builder, proxy);

        if (providerOnly || proxies.Length == 0)
            return builder.ToString();

        builder.AppendLine("proxy-groups:");
        builder.AppendLine("  - name: Proxy");
        builder.AppendLine("    type: select");
        builder.AppendLine("    proxies:");
        foreach (var proxy in proxies)
            builder.Append("      - ").AppendLine(Quote(proxy.Name));
        builder.AppendLine("rules:");
        builder.AppendLine("  - MATCH,Proxy");
        return builder.ToString();
    }

    private static MihomoProxy? CreateProxy(SubscriptionEndpoint endpoint) {
        return endpoint.Connection switch {
            Hysteria2ConnectionInfo hysteria2 => new MihomoProxy(endpoint.Name + " · Hysteria2", "hysteria2") {
                Server = hysteria2.ServerAddress, Port = hysteria2.ServerPort,
                Values = new Dictionary<string, string?> {
                    ["password"] = hysteria2.Password, ["sni"] = hysteria2.Sni,
                    ["skip-cert-verify"] = "false", ["fingerprint"] = hysteria2.PinSha256,
                    ["obfs"] = hysteria2.ObfsType is not null and not "none" ? hysteria2.ObfsType : null,
                    ["obfs-password"] = hysteria2.ObfsType is not null and not "none" ? hysteria2.ObfsPassword : null
                }
            },
            NaiveProxyConnectionInfo naive => new MihomoProxy(endpoint.Name + " · NaiveProxy", "naive") {
                Server = naive.ServerAddress, Port = naive.ServerPort,
                Values = new Dictionary<string, string?> {
                    ["username"] = naive.Username, ["password"] = naive.Password,
                    ["sni"] = naive.Sni,
                    ["skip-cert-verify"] = naive.Insecure && naive.PinSha256 is null ? "true" : "false",
                    ["fingerprint"] = naive.PinSha256
                }
            },
            WireGuardConnectionInfo wireGuard => new MihomoProxy(endpoint.Name + " · WireGuard", "wireguard") {
                Server = wireGuard.ServerAddress, Port = wireGuard.ServerPort,
                Values = new Dictionary<string, string?> {
                    ["ip"] = wireGuard.AssignedIp, ["private-key"] = wireGuard.PrivateKey,
                    ["public-key"] = wireGuard.ServerPublicKey, ["mtu"] = wireGuard.Mtu.ToString(),
                    ["allowed-ips"] = "['0.0.0.0/0']", ["udp"] = "true"
                }
            },
            _ => null
        };
    }

    private static void AppendProxy(StringBuilder builder, MihomoProxy proxy) {
        builder.Append("  - name: ").AppendLine(Quote(proxy.Name));
        builder.Append("    type: ").AppendLine(proxy.Type);
        builder.Append("    server: ").AppendLine(Quote(proxy.Server));
        builder.Append("    port: ").AppendLine(proxy.Port.ToString());
        foreach (var (key, value) in proxy.Values.Where(pair => pair.Value is not null)) {
            builder.Append("    ").Append(key).Append(": ").AppendLine(
                key is "skip-cert-verify" or "udp" or "mtu" or "allowed-ips" ? value : Quote(value!));
        }
    }

    private static string CreateNaiveProxyShareUrl(NaiveProxyConnectionInfo connection, string clientName) {
        var builder = new StringBuilder("naive+https://")
            .Append(Uri.EscapeDataString(connection.Username)).Append(':')
            .Append(Uri.EscapeDataString(connection.Password)).Append('@')
            .Append(connection.ServerAddress).Append(':').Append(connection.ServerPort).Append("/?");
        AddQuery(builder, "sni", connection.Sni);
        AddQuery(builder, "insecure", connection.Insecure ? "1" : null);
        AddQuery(builder, "pinSHA256", connection.PinSha256);
        AddQuery(builder, "name", clientName);
        return builder.ToString().TrimEnd('&');
    }

    private static void AddQuery(StringBuilder builder, string key, string? value) {
        if (!string.IsNullOrEmpty(value))
            builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value)).Append('&');
    }

    private static string Quote(string value) {
        return JsonSerializer.Serialize(value);
    }

    private sealed class MihomoProxy(string name, string type) {
        public string Name { get; } = name;
        public string Type { get; } = type;
        public required string Server { get; init; }
        public int Port { get; init; }
        public Dictionary<string, string?> Values { get; init; } = [];
    }
}