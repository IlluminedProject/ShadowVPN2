using ShadowVPN2.Contracts.Subscriptions;
using FreeTurnConnectionInfo = ShadowVPN2.Data.Subscription.Models.Connections.FreeTurnConnectionInfo;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public sealed class FreeTurnSubscriptionRenderer : ISubscriptionRenderer {
    public Models.Rules.SubscriptionResponseType ResponseType => Models.Rules.SubscriptionResponseType.FreeTurn;

    public SubscriptionFormatDescriptor Descriptor { get; } = new("freeturn", "FreeTurn", "text/plain; charset=utf-8");

    public string Render(SubscriptionResponse subscription) {
        var links = subscription.Protocols
            .SelectMany(protocol => protocol.Endpoints)
            .OrderByDescending(endpoint => endpoint.IsMain)
            .ThenBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(endpoint => endpoint.Address.ToString(), StringComparer.OrdinalIgnoreCase)
            .SelectMany(endpoint => endpoint.Transports.OfType<FreeTurnConnectionInfo>()
                .Select(transport => transport.CreateShareUrl(subscription.ClientName)))
            .ToArray();
        return string.Join('\n', links);
    }
}
