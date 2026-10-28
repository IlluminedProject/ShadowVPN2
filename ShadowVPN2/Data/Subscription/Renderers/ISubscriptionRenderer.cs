using ShadowVPN2.Contracts.Subscriptions;
using SubscriptionResponse = ShadowVPN2.Data.Subscription.Models.Common.SubscriptionResponse;

namespace ShadowVPN2.Data.Subscription.Renderers;

public interface ISubscriptionRenderer {
    Models.Rules.SubscriptionResponseType ResponseType { get; }
    SubscriptionFormatDescriptor Descriptor { get; }
    string Render(SubscriptionResponse subscription);
}
