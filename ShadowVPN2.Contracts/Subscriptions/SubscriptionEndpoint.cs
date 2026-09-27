namespace ShadowVPN2.Contracts.Subscriptions;

public sealed class SubscriptionEndpoint {
    public required string Name { get; set; }
    public required string Address { get; set; }
    public bool IsMain { get; set; }
    public required ProtocolConnectionInfo Connection { get; set; }
    public IReadOnlyList<TransportConnectionInfo> Transports { get; set; } = [];
}