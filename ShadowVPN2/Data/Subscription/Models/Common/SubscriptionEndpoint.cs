using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Subscription.Models.Connections;

namespace ShadowVPN2.Data.Subscription.Models.Common;

public class SubscriptionEndpoint {
    public required string Name { get; set; }
    public required HostAddress Address { get; set; }
    public bool IsMain { get; set; }
    public bool PublishDirectConnection { get; set; } = true;
    public required ProtocolConnectionInfo Connection { get; set; }
    public IReadOnlyList<TransportConnectionInfo> Transports { get; set; } = [];
}