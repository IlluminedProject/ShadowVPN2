using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.Subscription.Exporters;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class ClashSubscriptionRenderer : ISubscriptionRenderer {
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal) {
        "ss", "ssr", "socks5", "http", "vmess", "snell", "trojan"
    };

    private readonly SubscriptionProtocolExporterRegistry _exporters;

    public ClashSubscriptionRenderer() : this(SubscriptionProtocolExporterRegistry.Default) { }

    public ClashSubscriptionRenderer(SubscriptionProtocolExporterRegistry exporters) {
        _exporters = exporters;
    }

    public Models.Rules.SubscriptionResponseType ResponseType => Models.Rules.SubscriptionResponseType.Clash;

    public SubscriptionFormatDescriptor Descriptor { get; } = new("clash", "Clash", "text/yaml; charset=utf-8");

    public string Render(SubscriptionResponse subscription) =>
        YamlSubscriptionRendererSupport.Render(subscription, _exporters, Descriptor.Id,
            proxy => SupportedTypes.Contains(proxy.Type));
}
