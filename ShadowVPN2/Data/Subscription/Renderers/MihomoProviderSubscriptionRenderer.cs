using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.Subscription.Exporters;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class MihomoProviderSubscriptionRenderer : ISubscriptionRenderer {
    private readonly MihomoSubscriptionRenderer _renderer;

    public MihomoProviderSubscriptionRenderer() : this(SubscriptionProtocolExporterRegistry.Default) { }

    public MihomoProviderSubscriptionRenderer(SubscriptionProtocolExporterRegistry exporters) {
        _renderer = new MihomoSubscriptionRenderer(exporters);
    }

    public Models.Rules.SubscriptionResponseType ResponseType => Models.Rules.SubscriptionResponseType.MihomoProvider;

    public SubscriptionFormatDescriptor Descriptor { get; } = new("mihomo-provider", "Mihomo Provider", "text/yaml; charset=utf-8");

    public string Render(SubscriptionResponse subscription) => _renderer.Render(subscription, true);
}
