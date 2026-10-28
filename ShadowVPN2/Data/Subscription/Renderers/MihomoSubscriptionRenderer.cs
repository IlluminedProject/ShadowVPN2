using System.Text;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.Subscription.Exporters;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class MihomoSubscriptionRenderer : ISubscriptionRenderer {
    private readonly SubscriptionProtocolExporterRegistry _exporters;

    public MihomoSubscriptionRenderer() : this(SubscriptionProtocolExporterRegistry.Default) { }

    public MihomoSubscriptionRenderer(SubscriptionProtocolExporterRegistry exporters) {
        _exporters = exporters;
    }

    public Models.Rules.SubscriptionResponseType ResponseType => Models.Rules.SubscriptionResponseType.Mihomo;

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
                .CreateMihomoProxy(endpoint.Connection, endpoint.Name) ??
                throw new ArgumentException($"Endpoint '{endpoint.Name}' cannot be represented in the Mihomo format.", nameof(subscription)))
            .ToArray();
        var builder = new StringBuilder(proxies.Length == 0 ? "proxies: []\n" : "proxies:\n");
        foreach (var proxy in proxies)
            YamlSubscriptionRendererSupport.AppendProxy(builder, proxy);

        if (providerOnly || proxies.Length == 0)
            return builder.ToString();

        builder.AppendLine("proxy-groups:");
        builder.AppendLine("  - name: Proxy");
        builder.AppendLine("    type: select");
        builder.AppendLine("    proxies:");
        foreach (var proxy in proxies)
            builder.Append("      - ").AppendLine(System.Text.Json.JsonSerializer.Serialize(proxy.Name));
        builder.AppendLine("rules:");
        builder.AppendLine("  - MATCH,Proxy");
        return builder.ToString();
    }

}
