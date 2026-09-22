using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.Subscription;

public class SubscriptionService(
    IAsyncDocumentSession session,
    NodeService nodeService,
    NodeNetworkService nodeNetworkService,
    ManagedCertificateService managedCertificateService,
    GlobalConfigurationService globalConfigService,
    FreeTurnClientIdService freeTurnClientIdService,
    IEnumerable<ISubscriptionConnectionContributor> contributors) {
    public async Task<SubscriptionResponse?> GetSubscriptionAsync(Guid subscriptionId) {
        var client = await session.Query<EntityClient>()
            .FirstOrDefaultAsync(c => c.SubscriptionId == subscriptionId);

        if (client is null) return null;

        var globalConfig = await globalConfigService.GetAsync();
        var nodes = await nodeService.GetNodesAsync();
        var freeTurnTransports = globalConfig.Transports
            .OfType<FreeTurnTransportSettings>()
            .Where(transport => transport.Enabled)
            .ToList();
        var protocolSubscriptions = new List<ProtocolSubscription>();

        foreach (var settings in globalConfig.Protocols.Where(p => p.Enabled)) {
            var contributor = contributors.FirstOrDefault(c =>
                c.Protocol.Equals(settings.Protocol, StringComparison.OrdinalIgnoreCase));
            if (contributor is null) continue;

            var endpoints = new List<SubscriptionEndpoint>();
            var mainDomain = settings.MainDomain ?? globalConfig.MainDomain;
            if (!string.IsNullOrWhiteSpace(mainDomain)) {
                var mainEndpoint = await CreateEndpointAsync(
                    contributor,
                    client,
                    settings,
                    HostAddress.Parse(mainDomain),
                    HostAddress.Parse(mainDomain),
                    mainDomain,
                    true,
                    "Main");
                if (mainEndpoint is not null) {
                    endpoints.Add(mainEndpoint);
                    AddTransportConnections(mainEndpoint, settings, freeTurnTransports, client,
                        HostAddress.Parse(mainDomain));
                }
            }

            foreach (var node in nodes.Where(n => !n.JoinSecret.HasValue)) {
                var publicHost = await nodeNetworkService.GetPublicHostAsync(node);
                if (string.IsNullOrWhiteSpace(publicHost)) continue;
                var host = HostAddress.Parse(publicHost);
                var certificate = managedCertificateService.GetForNode(node.NodeId);
                var sni = !string.IsNullOrWhiteSpace(node.Domain) &&
                          certificate?.Domains.Contains(node.Domain, StringComparer.OrdinalIgnoreCase) == true
                    ? node.Domain
                    : certificate?.Domains.FirstOrDefault() ?? host.ToString();
                var nodeEndpoint = await CreateEndpointAsync(
                    contributor,
                    client,
                    settings,
                    host,
                    host,
                    sni,
                    false,
                    string.IsNullOrWhiteSpace(node.Name) ? host.ToString() : node.Name);
                if (nodeEndpoint is not null) {
                    endpoints.Add(nodeEndpoint);
                    AddTransportConnections(nodeEndpoint, settings, freeTurnTransports, client, host);
                }
            }

            if (endpoints.Count > 0) {
                protocolSubscriptions.Add(new ProtocolSubscription {
                    Protocol = settings.Protocol,
                    Endpoints = endpoints.AsReadOnly()
                });
            }
        }

        return new SubscriptionResponse {
            ClientName = client.Name,
            Protocols = protocolSubscriptions.AsReadOnly()
        };
    }

    private static async Task<SubscriptionEndpoint?> CreateEndpointAsync(
        ISubscriptionConnectionContributor contributor,
        EntityClient client,
        ProtocolGlobalSettings settings,
        HostAddress address,
        HostAddress host,
        string sni,
        bool isMain,
        string name) {
        var connection = await contributor.CreateAsync(client, settings, host.Value, sni);
        if (connection is null)
            return null;

        return new SubscriptionEndpoint {
            Name = name,
            Address = address,
            IsMain = isMain,
            Connection = connection
        };
    }

    private void AddTransportConnections(
        SubscriptionEndpoint directEndpoint,
        ProtocolGlobalSettings protocol,
        IReadOnlyList<FreeTurnTransportSettings> transports,
        EntityClient client,
        HostAddress publicHost) {
        var transportConnections = directEndpoint.Transports.ToList();
        foreach (var freeTurn in transports.Where(transport => transport.ProtocolId == protocol.Id)) {
            var mode = ProtocolDefinitionMetadata.GetSocketKind(protocol);
            var relayAddress = publicHost.FormatWithPort(freeTurn.ListenPort);
            var transportInfo = new FreeTurnConnectionInfo {
                Peer = relayAddress,
                ObfuscationProfile = freeTurn.ObfuscationProfile,
                ObfuscationKey = freeTurn.ObfuscationKey,
                TurnTransport = freeTurn.TurnTransport,
                Mode = mode,
                Streams = freeTurn.Streams,
                ClientId = freeTurnClientIdService.GetClientId(client.SubscriptionId)
            };
            transportConnections.Add(transportInfo);
        }

        directEndpoint.Transports = transportConnections.AsReadOnly();
    }
}