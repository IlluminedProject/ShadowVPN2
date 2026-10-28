using ShadowVPN2.Data.Subscription.Models.Common;
using ShadowVPN2.Data.Subscription.Models.Rules;
using ShadowVPN2.Data.Subscription.Renderers;

namespace ShadowVPN2.Data.Subscription.Services;

public sealed class SubscriptionFormatService {
    private readonly SubscriptionRendererRegistry _registry;

    public SubscriptionFormatService() : this(SubscriptionRendererRegistry.Default) { }

    public SubscriptionFormatService(SubscriptionRendererRegistry registry) {
        _registry = registry;
    }

    public string Render(SubscriptionResponse subscription, SubscriptionFormat format) {
        var responseType = format switch {
            SubscriptionFormat.Base64 => SubscriptionResponseType.Base64,
            SubscriptionFormat.Mihomo => SubscriptionResponseType.Mihomo,
            SubscriptionFormat.MihomoProvider => SubscriptionResponseType.MihomoProvider,
            SubscriptionFormat.FreeTurn => SubscriptionResponseType.FreeTurn,
            SubscriptionFormat.Json or SubscriptionFormat.XrayJson => SubscriptionResponseType.XrayJson,
            SubscriptionFormat.Stash => SubscriptionResponseType.Stash,
            SubscriptionFormat.Clash => SubscriptionResponseType.Clash,
            SubscriptionFormat.SingBox => SubscriptionResponseType.SingBox,
            _ => throw new ArgumentException($"Unsupported subscription format: {format}", nameof(format))
        };
        if (!_registry.TryGet(responseType, out var renderer))
            throw new ArgumentException($"Unsupported subscription format: {format}", nameof(format));
        return renderer.Render(subscription);
    }
}