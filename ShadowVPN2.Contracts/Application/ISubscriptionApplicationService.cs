using ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Contracts.Application;

public interface ISubscriptionApplicationService {
    Task<SubscriptionResponse?> GetSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);
}