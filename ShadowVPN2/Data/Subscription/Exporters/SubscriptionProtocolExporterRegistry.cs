using ShadowVPN2.Data.Subscription.Models.Connections;

namespace ShadowVPN2.Data.Subscription.Exporters;

public sealed class SubscriptionProtocolExporterRegistry(IEnumerable<ISubscriptionProtocolExporter> exporters) {
    private readonly ISubscriptionProtocolExporter[] _exporters = exporters.ToArray();

    public static SubscriptionProtocolExporterRegistry Default { get; } = new([
        new Hysteria2SubscriptionProtocolExporter(),
        new NaiveProxySubscriptionProtocolExporter(),
        new WireGuardSubscriptionProtocolExporter()
    ]);

    public ISubscriptionProtocolExporter GetRequired(ProtocolConnectionInfo connection) =>
        TryGet(connection, out var exporter)
            ? exporter
            : throw new ArgumentException($"Unsupported subscription protocol: {connection.GetType().Name}", nameof(connection));

    public bool TryGet(ProtocolConnectionInfo connection, out ISubscriptionProtocolExporter exporter) {
        foreach (var candidate in _exporters) {
            if (!candidate.CanExport(connection))
                continue;
            exporter = candidate;
            return true;
        }
        exporter = null!;
        return false;
    }
}
