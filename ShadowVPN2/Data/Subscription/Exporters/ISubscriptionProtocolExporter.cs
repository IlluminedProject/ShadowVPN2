using ShadowVPN2.Data.Subscription.Models.Connections;
using ShadowVPN2.Data.Subscription.Models.Mihomo;
using Contract = ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Data.Subscription.Exporters;

public interface ISubscriptionProtocolExporter {
    bool CanExport(ProtocolConnectionInfo connection);
    string CreateShareUrl(ProtocolConnectionInfo connection, string clientName);
    MihomoProxy? CreateMihomoProxy(ProtocolConnectionInfo connection, string endpointName);
    Contract.ProtocolConnectionInfo ToContract(ProtocolConnectionInfo connection);
}
