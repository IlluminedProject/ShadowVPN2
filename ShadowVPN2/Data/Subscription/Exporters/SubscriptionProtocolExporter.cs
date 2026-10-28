using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Data.Subscription.Models.Connections;
using ShadowVPN2.Data.Subscription.Models.Mihomo;
using ShadowVPN2.Data.Subscription.Models.Xray;
using Contract = ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Data.Subscription.Exporters;

public abstract class SubscriptionProtocolExporter<T> : ISubscriptionProtocolExporter,
    ISingBoxSubscriptionExporter, IXraySubscriptionExporter where T : ProtocolConnectionInfo {
    public bool CanExport(ProtocolConnectionInfo connection) => connection is T;

    string ISubscriptionProtocolExporter.CreateShareUrl(ProtocolConnectionInfo connection, string clientName) =>
        CreateShareUrl(GetConnection(connection), clientName);

    MihomoProxy? ISubscriptionProtocolExporter.CreateMihomoProxy(ProtocolConnectionInfo connection, string endpointName) =>
        CreateMihomoProxy(GetConnection(connection), endpointName);

    Contract.ProtocolConnectionInfo ISubscriptionProtocolExporter.ToContract(ProtocolConnectionInfo connection) =>
        ToContract(GetConnection(connection));

    OutboundConfig? ISingBoxSubscriptionExporter.CreateSingBoxOutbound(ProtocolConnectionInfo connection) =>
        CreateSingBoxOutbound(GetConnection(connection));

    EndpointConfig? ISingBoxSubscriptionExporter.CreateSingBoxEndpoint(ProtocolConnectionInfo connection) =>
        CreateSingBoxEndpoint(GetConnection(connection));

    public virtual EndpointConfig? CreateSingBoxEndpoint(T connection) => null;

    XrayOutboundConfig? IXraySubscriptionExporter.CreateXrayOutbound(ProtocolConnectionInfo connection) =>
        CreateXrayOutbound(GetConnection(connection));

    public virtual OutboundConfig? CreateSingBoxOutbound(T connection) => null;
    public virtual XrayOutboundConfig? CreateXrayOutbound(T connection) => null;

    public abstract string CreateShareUrl(T connection, string clientName);
    public abstract MihomoProxy? CreateMihomoProxy(T connection, string endpointName);
    public abstract Contract.ProtocolConnectionInfo ToContract(T connection);

    private static T GetConnection(ProtocolConnectionInfo connection) =>
        connection as T ?? throw new ArgumentException($"Expected connection of type {typeof(T).Name}", nameof(connection));
}
