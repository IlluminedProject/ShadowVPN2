using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Subscription.Models.Connections;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.Subscription.Contributors;

public interface ISubscriptionConnectionContributor {
    string Protocol { get; }

    Task<ProtocolConnectionInfo?> CreateAsync(
        EntityClient client,
        ProtocolGlobalSettings settings,
        HostName host,
        HostName sni,
        CancellationToken cancellationToken = default);
}