using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.Subscription;

public interface ISubscriptionConnectionContributor {
    string Protocol { get; }

    Task<ProtocolConnectionInfo?> CreateAsync(
        EntityClient client,
        ProtocolGlobalSettings settings,
        string host,
        string sni,
        CancellationToken cancellationToken = default);
}