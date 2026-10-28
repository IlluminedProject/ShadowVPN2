namespace ShadowVPN2.Data.Subscription.Models.Rules;

public sealed record SubscriptionResponseRule {
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public SubscriptionResponseType ResponseType { get; init; } = SubscriptionResponseType.Base64;
    public IReadOnlyList<SubscriptionHeaderCondition> Conditions { get; init; } = Array.Empty<SubscriptionHeaderCondition>();
}
