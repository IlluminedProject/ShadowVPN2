namespace ShadowVPN2.Contracts.Subscriptions;

public sealed class ProtocolSubscription {
    public required string Protocol { get; set; }
    public required IReadOnlyList<SubscriptionEndpoint> Endpoints { get; set; }
}