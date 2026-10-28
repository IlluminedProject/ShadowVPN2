using System.Globalization;
using System.Text;
using System.Text.Json;
using ShadowVPN2.Data.Subscription.Exporters;
using ShadowVPN2.Data.Subscription.Models.Common;
using ShadowVPN2.Data.Subscription.Models.Mihomo;

namespace ShadowVPN2.Data.Subscription.Renderers;

internal static class YamlSubscriptionRendererSupport {
    internal static string Render(
        SubscriptionResponse subscription,
        SubscriptionProtocolExporterRegistry exporters,
        string formatId,
        Func<MihomoProxy, bool> canRepresent,
        bool stash = false) {
        var proxies = new List<MihomoProxy>();
        foreach (var endpoint in subscription.Protocols
                     .SelectMany(protocol => protocol.Endpoints)
                     .Where(endpoint => endpoint.PublishDirectConnection)
                     .OrderByDescending(endpoint => endpoint.IsMain)
                     .ThenBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(endpoint => endpoint.Address.ToString(), StringComparer.OrdinalIgnoreCase)) {
            var proxy = exporters.GetRequired(endpoint.Connection)
                .CreateMihomoProxy(endpoint.Connection, endpoint.Name);
            if (proxy is null || !canRepresent(proxy))
                continue;
            proxies.Add(proxy);
        }

        if (proxies.Count == 0)
            throw new ArgumentException(
                $"The subscription has no direct endpoints representable in the '{formatId}' format.",
                nameof(subscription));

        var builder = new StringBuilder("proxies:\n");
        foreach (var proxy in proxies)
            AppendProxy(builder, proxy, stash);

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

    internal static void AppendProxy(StringBuilder builder, MihomoProxy proxy, bool stash = false) {
        builder.Append("  - name: ").AppendLine(Quote(proxy.Name));
        builder.Append("    type: ").AppendLine(proxy.Type);
        builder.Append("    server: ").AppendLine(Quote(proxy.Server));
        builder.Append("    port: ").AppendLine(proxy.Port.ToString(CultureInfo.InvariantCulture));
        AppendString(builder, "password", stash && proxy.Type == "hysteria2" ? null : proxy.Password);
        if (stash && proxy.Type == "hysteria2")
            AppendString(builder, "auth", proxy.Password);
        AppendString(builder, "username", proxy.Username);
        AppendString(builder, "sni", proxy.Sni);
        AppendBoolean(builder, "skip-cert-verify", proxy.SkipCertVerify);
        if (stash)
            AppendString(builder, "server-cert-fingerprint",
                proxy.Fingerprint is null ? null : NormalizeFingerprint(proxy.Fingerprint));
        else
            AppendString(builder, "fingerprint", proxy.Fingerprint);
        AppendString(builder, "obfs", proxy.Obfs);
        AppendString(builder, "obfs-password", proxy.ObfsPassword);
        AppendString(builder, "ports", proxy.Ports);
        AppendString(builder, "ip", proxy.Ip);
        AppendString(builder, "private-key", proxy.PrivateKey);
        AppendString(builder, "public-key", proxy.PublicKey);
        if (proxy.Mtu is { } mtu)
            builder.Append("    mtu: ").AppendLine(mtu.ToString(CultureInfo.InvariantCulture));
        if (!stash || proxy.Type != "wireguard") {
            if (proxy.AllowedIps is { } allowedIps) {
                builder.Append("    allowed-ips: [");
                for (var index = 0; index < allowedIps.Count; index++) {
                    if (index > 0)
                        builder.Append(", ");
                    builder.Append(Quote(allowedIps[index]));
                }

                builder.AppendLine("]");
            }

            AppendBoolean(builder, "udp", proxy.Udp);
        }
    }

    internal static string? NormalizeFingerprint(string value) {
        var hex = value.Replace(":", string.Empty, StringComparison.Ordinal);
        if (hex.Length == 64 && hex.All(Uri.IsHexDigit))
            return hex;

        Span<byte> bytes = stackalloc byte[32];
        return Convert.TryFromBase64String(value, bytes, out var written) && written == bytes.Length
            ? Convert.ToHexString(bytes)
            : null;
    }

    private static void AppendString(StringBuilder builder, string field, string? value) {
        if (value is not null)
            builder.Append("    ").Append(field).Append(": ").AppendLine(Quote(value));
    }

    private static void AppendBoolean(StringBuilder builder, string field, bool? value) {
        if (value is { } boolean)
            builder.Append("    ").Append(field).Append(": ").AppendLine(boolean ? "true" : "false");
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);
}