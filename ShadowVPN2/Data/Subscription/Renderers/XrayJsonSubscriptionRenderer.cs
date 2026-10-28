using System.Text.Json;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.Subscription.Exporters;
using ShadowVPN2.Data.Subscription.Models.Xray;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class XrayJsonSubscriptionRenderer : ISubscriptionRenderer {
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SubscriptionProtocolExporterRegistry _exporters;

    public XrayJsonSubscriptionRenderer() : this(SubscriptionProtocolExporterRegistry.Default) { }

    public XrayJsonSubscriptionRenderer(SubscriptionProtocolExporterRegistry exporters) {
        _exporters = exporters;
    }

    public Models.Rules.SubscriptionResponseType ResponseType => Models.Rules.SubscriptionResponseType.XrayJson;

    public SubscriptionFormatDescriptor Descriptor { get; } = new("xray-json", "Xray JSON", "application/json; charset=utf-8");

    public string Render(SubscriptionResponse subscription) {
        var config = new XraySubscriptionConfig();
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in subscription.Protocols
                     .SelectMany(protocol => protocol.Endpoints)
                     .Where(endpoint => endpoint.PublishDirectConnection)
                     .OrderByDescending(endpoint => endpoint.IsMain)
                     .ThenBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(endpoint => endpoint.Address.ToString(), StringComparer.OrdinalIgnoreCase)) {
            var exporter = _exporters.GetRequired(endpoint.Connection);
            var outbound = (exporter as IXraySubscriptionExporter)?.CreateXrayOutbound(endpoint.Connection);
            if (outbound is null)
                throw new ArgumentException(
                    $"Endpoint '{endpoint.Name}' ({endpoint.Connection.GetType().Name}) cannot be represented in the xray-json subscription format.",
                    nameof(subscription));

            var name = string.IsNullOrWhiteSpace(endpoint.Name) ? "Proxy" : endpoint.Name;
            var tag = name;
            for (var suffix = 2; !tags.Add(tag); suffix++)
                tag = $"{name} ({suffix})";
            outbound.Tag = tag;
            config.Outbounds.Add(outbound);
        }

        if (config.Outbounds.Count == 0)
            throw new ArgumentException("The subscription has no direct endpoints representable in the xray-json format.", nameof(subscription));

        return JsonSerializer.Serialize(config, JsonOptions);
    }
}
