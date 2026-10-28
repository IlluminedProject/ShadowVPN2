using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.Subscription.Exporters;
using ShadowVPN2.Data.Subscription.Models.Mihomo;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class StashSubscriptionRenderer : ISubscriptionRenderer {
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal) {
        "ss", "ssr", "socks5", "http", "vmess", "snell", "trojan", "hysteria2", "wireguard"
    };

    private readonly SubscriptionProtocolExporterRegistry _exporters;

    public StashSubscriptionRenderer() : this(SubscriptionProtocolExporterRegistry.Default) { }

    public StashSubscriptionRenderer(SubscriptionProtocolExporterRegistry exporters) {
        _exporters = exporters;
    }

    public Models.Rules.SubscriptionResponseType ResponseType => Models.Rules.SubscriptionResponseType.Stash;

    public SubscriptionFormatDescriptor Descriptor { get; } = new("stash", "Stash", "text/yaml; charset=utf-8");

    public string Render(SubscriptionResponse subscription) =>
        YamlSubscriptionRendererSupport.Render(subscription, _exporters, Descriptor.Id, CanRepresent, stash: true);

    private static bool CanRepresent(MihomoProxy proxy) {
        if (!SupportedTypes.Contains(proxy.Type))
            return false;
        // Stash's Hysteria2 schema does not expose Mihomo's obfuscation options.
        if (proxy.Type == "hysteria2" && (proxy.Obfs is not null || proxy.ObfsPassword is not null))
            return false;
        // Stash WireGuard routes the full tunnel and has no per-proxy allowed-ips/udp fields.
        if (proxy.Type == "wireguard" &&
            (proxy.Udp == false || proxy.AllowedIps is { } allowedIps &&
                (allowedIps.Count != 1 || allowedIps[0] != "0.0.0.0/0")))
            return false;

        return proxy.Fingerprint is null ||
               YamlSubscriptionRendererSupport.NormalizeFingerprint(proxy.Fingerprint) is not null;
    }
}
