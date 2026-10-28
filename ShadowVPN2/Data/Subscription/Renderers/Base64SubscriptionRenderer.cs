using System.Text;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.Subscription.Exporters;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class Base64SubscriptionRenderer : ISubscriptionRenderer {
    private readonly SubscriptionProtocolExporterRegistry _exporters;

    public Base64SubscriptionRenderer() : this(SubscriptionProtocolExporterRegistry.Default) { }

    public Base64SubscriptionRenderer(SubscriptionProtocolExporterRegistry exporters) {
        _exporters = exporters;
    }

    public Models.Rules.SubscriptionResponseType ResponseType => Models.Rules.SubscriptionResponseType.Base64;

    public SubscriptionFormatDescriptor Descriptor { get; } = new("base64", "Base64", "text/plain; charset=utf-8");

    public string Render(SubscriptionResponse subscription) {
        var links = subscription.Protocols
            .SelectMany(protocol => protocol.Endpoints
            .Where(endpoint => endpoint.PublishDirectConnection)
            .Select(endpoint =>                (protocol, endpoint, connection: endpoint.Connection)))
            .OrderByDescending(item => item.endpoint.IsMain)
            .ThenBy(item => item.endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.endpoint.Address.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(item => _exporters.GetRequired(item.connection).CreateShareUrl(item.connection, subscription.ClientName))
            .ToArray();

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', links)));
    }

}
