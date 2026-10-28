using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.Subscription.Exporters;
using ShadowVPN2.Data.Subscription.Models.Rules;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class SubscriptionRendererRegistry {
    private readonly Dictionary<SubscriptionResponseType, ISubscriptionRenderer> _renderers = new();

    public static SubscriptionRendererRegistry Default { get; } = new([
        new Base64SubscriptionRenderer(SubscriptionProtocolExporterRegistry.Default),
        new MihomoSubscriptionRenderer(SubscriptionProtocolExporterRegistry.Default),
        new MihomoProviderSubscriptionRenderer(SubscriptionProtocolExporterRegistry.Default),
        new FreeTurnSubscriptionRenderer(),
        new StashSubscriptionRenderer(SubscriptionProtocolExporterRegistry.Default),
        new ClashSubscriptionRenderer(SubscriptionProtocolExporterRegistry.Default),
        new SingBoxSubscriptionRenderer(SubscriptionProtocolExporterRegistry.Default),
        new XrayJsonSubscriptionRenderer(SubscriptionProtocolExporterRegistry.Default)
    ]);

    public SubscriptionRendererRegistry(IEnumerable<ISubscriptionRenderer> renderers) {
        foreach (var renderer in renderers)
            _renderers.Add(renderer.ResponseType, renderer);
        Formats = Array.AsReadOnly(_renderers.Values.Select(renderer => renderer.Descriptor).ToArray());
    }

    public IReadOnlyList<SubscriptionFormatDescriptor> Formats { get; }

    public bool TryGet(SubscriptionResponseType responseType, out ISubscriptionRenderer renderer) =>
        _renderers.TryGetValue(responseType, out renderer!);
}
