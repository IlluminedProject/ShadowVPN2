using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Data.Subscription.Models.Connections;

namespace ShadowVPN2.Data.Subscription.Exporters;

public interface ISingBoxSubscriptionExporter {
    /// <summary>Creates a fresh sing-box outbound object, or returns null when the connection requires an endpoint or cannot be represented.</summary>
    OutboundConfig? CreateSingBoxOutbound(ProtocolConnectionInfo connection) => null;

    /// <summary>Creates a fresh sing-box endpoint object, or returns null when the connection uses an outbound or cannot be represented.</summary>
    EndpointConfig? CreateSingBoxEndpoint(ProtocolConnectionInfo connection) => null;
}
