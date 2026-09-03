namespace ShadowVPN2.Data.Subscription;

public class ProtocolSubscription {
    public required string Protocol { get; set; }
    public required IReadOnlyList<SubscriptionEndpoint> Endpoints { get; set; }
}