namespace ShadowVPN2.Data.Subscription;

public class SubscriptionResponse {
    public required string ClientName { get; set; }
    public required IReadOnlyList<ProtocolSubscription> Protocols { get; set; }
}