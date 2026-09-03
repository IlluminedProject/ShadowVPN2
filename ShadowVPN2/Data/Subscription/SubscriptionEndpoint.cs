namespace ShadowVPN2.Data.Subscription;

public class SubscriptionEndpoint {
    public required string Name { get; set; }
    public bool IsMain { get; set; }
    public required ProtocolConnectionInfo Connection { get; set; }
}