using ShadowVPN2.Data.Subscription.Models.Connections;
using ShadowVPN2.Data.Subscription.Models.Xray;

namespace ShadowVPN2.Data.Subscription.Exporters;

public interface IXraySubscriptionExporter {
    /// <summary>Creates a fresh Xray outbound object, or returns null when the connection cannot be represented.</summary>
    XrayOutboundConfig? CreateXrayOutbound(ProtocolConnectionInfo connection);
}
