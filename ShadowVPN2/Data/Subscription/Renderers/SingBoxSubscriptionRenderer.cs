using System.Text.Json;
using System.Text.Json.Serialization;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Data.Subscription.Exporters;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class SingBoxSubscriptionRenderer : ISubscriptionRenderer {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly SubscriptionProtocolExporterRegistry _exporters;

    public SingBoxSubscriptionRenderer() : this(SubscriptionProtocolExporterRegistry.Default) { }

    public SingBoxSubscriptionRenderer(SubscriptionProtocolExporterRegistry exporters) {
        _exporters = exporters;
    }

    public Models.Rules.SubscriptionResponseType ResponseType => Models.Rules.SubscriptionResponseType.SingBox;

    public SubscriptionFormatDescriptor Descriptor { get; } = new("singbox", "sing-box", "application/json; charset=utf-8");

    public string Render(SubscriptionResponse subscription) {
        const string selectorTag = "Proxy";
        var selector = new SelectorOutboundConfig { Tag = selectorTag };
        var configuration = new SingBoxClientConfig {
            Outbounds = { selector },
            Route = new SingBoxClientRouteConfig { Final = selectorTag }
        };
        var tags = new HashSet<string>(StringComparer.Ordinal) { selectorTag };
        foreach (var endpoint in subscription.Protocols
                     .SelectMany(protocol => protocol.Endpoints)
                     .Where(endpoint => endpoint.PublishDirectConnection)
                     .OrderByDescending(endpoint => endpoint.IsMain)
                     .ThenBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(endpoint => endpoint.Address.ToString(), StringComparer.OrdinalIgnoreCase)) {
            var exporter = _exporters.GetRequired(endpoint.Connection) as ISingBoxSubscriptionExporter;
            var outbound = exporter?.CreateSingBoxOutbound(endpoint.Connection);
            var singBoxEndpoint = outbound is null ? exporter?.CreateSingBoxEndpoint(endpoint.Connection) : null;
            if (outbound is null && singBoxEndpoint is null)
                throw new ArgumentException(
                    $"Endpoint '{endpoint.Name}' ({endpoint.Connection.GetType().Name}) cannot be represented in the singbox subscription format.",
                    nameof(subscription));

            var name = string.IsNullOrWhiteSpace(endpoint.Name) ? "Proxy" : endpoint.Name;
            var tag = name;
            for (var suffix = 2; !tags.Add(tag); suffix++)
                tag = $"{name} ({suffix})";
            selector.Outbounds.Add(tag);
            if (outbound is not null) {
                outbound.Tag = tag;
                configuration.Outbounds.Add(outbound);
            } else {
                singBoxEndpoint!.Tag = tag;
                (configuration.Endpoints ??= new List<EndpointConfig>()).Add(singBoxEndpoint);
            }
        }

        if (selector.Outbounds.Count == 0)
            throw new ArgumentException("The subscription has no direct endpoints representable in the singbox format.", nameof(subscription));

        return JsonSerializer.Serialize(configuration, JsonOptions);
    }
}
