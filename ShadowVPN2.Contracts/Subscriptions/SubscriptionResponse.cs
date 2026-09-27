namespace ShadowVPN2.Contracts.Subscriptions;

public sealed class SubscriptionResponse {
    public required string ClientName { get; set; }
    public required IReadOnlyList<ProtocolSubscription> Protocols { get; set; }
}