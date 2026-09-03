namespace ShadowVPN2.Data.Subscription;

public sealed class SubscriptionEndpointContext {
    public required string Host { get; init; }
    public required string Sni { get; init; }
    public bool IsMain { get; init; }
}