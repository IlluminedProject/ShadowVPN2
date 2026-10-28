using ShadowVPN2.Data.Subscription.Models.Common;

namespace ShadowVPN2.Data.Subscription.Services;

public interface ISubscriptionSource {
    Task<SubscriptionResponse?> GetSubscriptionAsync(Guid subscriptionId);
}
