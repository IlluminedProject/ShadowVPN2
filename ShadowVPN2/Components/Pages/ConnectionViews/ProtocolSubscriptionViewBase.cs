using Microsoft.AspNetCore.Components;
using ShadowVPN2.Data.Subscription;

namespace ShadowVPN2.Components.Pages.ConnectionViews;

public abstract class ProtocolSubscriptionViewBase<TConnectionInfo> : ComponentBase
    where TConnectionInfo : ProtocolConnectionInfo {
    [CascadingParameter] protected ProtocolSubscription Subscription { get; set; } = null!;

    protected IEnumerable<(string Name, TConnectionInfo Connection)> GetEndpoints() {
        foreach (var endpoint in Subscription.Endpoints) {
            if (endpoint.Connection is TConnectionInfo connection)
                yield return (endpoint.Name, connection);
        }
    }
}