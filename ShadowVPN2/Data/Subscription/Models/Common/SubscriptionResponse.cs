namespace ShadowVPN2.Data.Subscription.Models.Common;

public class SubscriptionResponse {
    public required string ClientName { get; set; }
    public required IReadOnlyList<ProtocolSubscription> Protocols { get; set; }
}