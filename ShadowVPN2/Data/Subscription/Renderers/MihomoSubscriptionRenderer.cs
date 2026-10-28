using System.Text;
using System.Text.Json;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.Subscription.Exporters;
using ShadowVPN2.Data.Subscription.Models.Mihomo;
using ShadowVPN2.Data.Subscription.Models.Rules;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class MihomoSubscriptionRenderer : ISubscriptionRenderer {
    private readonly SubscriptionProtocolExporterRegistry _exporters;

    public MihomoSubscriptionRenderer() : this(SubscriptionProtocolExporterRegistry.Default) { }

    public MihomoSubscriptionRenderer(SubscriptionProtocolExporterRegistry exporters) {
        _exporters = exporters;
    }

    public SubscriptionResponseType ResponseType {
        get => SubscriptionResponseType.Mihomo;
    }

    public SubscriptionFormatDescriptor Descriptor { get; } = new("mihomo", "Mihomo", "text/yaml; charset=utf-8");

    public string Render(SubscriptionResponse subscription) => Render(subscription, false);

    internal string Render(SubscriptionResponse subscription, bool providerOnly) {
        var proxies = subscription.Protocols
            .SelectMany(protocol => protocol.Endpoints)
            .Where(endpoint => endpoint.PublishDirectConnection)
            .OrderByDescending(endpoint => endpoint.IsMain)
            .ThenBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(endpoint => endpoint.Address.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(endpoint => _exporters.GetRequired(endpoint.Connection)
                .CreateMihomoProxy(endpoint.Connection, endpoint.Name))
            .OfType<MihomoProxy>()
            .ToArray();
        if (proxies.Length == 0) {
            throw new ArgumentException("The subscription has no direct endpoints representable in the Mihomo format.",
                nameof(subscription));
        }

        var builder = new StringBuilder("proxies:\n");
        foreach (var proxy in proxies)
            YamlSubscriptionRendererSupport.AppendProxy(builder, proxy);

        if (providerOnly)
            return builder.ToString();

        builder.AppendLine("proxy-groups:");
        builder.AppendLine("  - name: Proxy");
        builder.AppendLine("    type: select");
        builder.AppendLine("    proxies:");
        foreach (var proxy in proxies)
            builder.Append("      - ").AppendLine(JsonSerializer.Serialize(proxy.Name));
        builder.AppendLine("rules:");
        builder.AppendLine("  - MATCH,Proxy");
        return builder.ToString();
    }
}