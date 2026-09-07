using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Data.Certificates;

namespace ShadowVPN2.Data.Subscription;

public class SubscriptionService(
    IAsyncDocumentSession session,
    NodeService nodeService,
    NodeNetworkService nodeNetworkService,
    ManagedCertificateService managedCertificateService,
    GlobalConfigurationService globalConfigService,
    IEnumerable<ISubscriptionConnectionContributor> contributors) {
    public async Task<SubscriptionResponse?> GetSubscriptionAsync(Guid subscriptionId) {
        var client = await session.Query<EntityClient>()
            .FirstOrDefaultAsync(c => c.SubscriptionId == subscriptionId);

        if (client is null) return null;

        var globalConfig = await globalConfigService.GetAsync();
        var nodes = await nodeService.GetNodesAsync();
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
                    mainDomain,
                    EndpointAddress.GetHost(mainDomain),
                    EndpointAddress.GetHost(mainDomain),
                    true,
                    "Main");
                if (mainEndpoint is not null) {
                    endpoints.Add(mainEndpoint);
                }
            }

            foreach (var node in nodes.Where(n => !n.JoinSecret.HasValue)) {
                var publicHost = await nodeNetworkService.GetPublicHostAsync(node);
                if (string.IsNullOrWhiteSpace(publicHost)) continue;
                var host = EndpointAddress.GetHost(publicHost);
                var certificate = managedCertificateService.GetForNode(node.NodeId);
                var sni = !string.IsNullOrWhiteSpace(node.Domain) &&
                          certificate?.Domains.Contains(node.Domain, StringComparer.OrdinalIgnoreCase) == true
                    ? node.Domain
                    : certificate?.Domains.FirstOrDefault() ?? host;
                var nodeEndpoint = await CreateEndpointAsync(
                    contributor,
                    client,
                    settings,
                    publicHost,
                    sni,
                    host,
                    false,
                    string.IsNullOrWhiteSpace(node.Name) ? host : node.Name);
                if (nodeEndpoint is not null) {
                    endpoints.Add(nodeEndpoint);
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
        string address,
        string host,
        string sni,
        bool isMain,
        string name) {
        var connection = await contributor.CreateAsync(client, settings, host, sni);
        if (connection is null)
            return null;

        return new SubscriptionEndpoint {
            Name = name,
            Address = address,
            IsMain = isMain,
            Connection = connection
        };
    }
}