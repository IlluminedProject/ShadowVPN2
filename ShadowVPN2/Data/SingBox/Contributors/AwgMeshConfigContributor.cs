using Microsoft.Extensions.Options;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.SingBox.Contributors;

public class AwgMeshConfigContributor(
    NodeService nodeService,
    NodeNetworkService nodeNetworkService,
    IOptions<LocalConfiguration> localConfiguration,
    GlobalConfigurationService globalConfigurationService,
    IOptions<SingBoxOptions> options) : ISingBoxConfigContributor {
    public async Task ContributeAsync(SingBoxConfig config, IReadOnlyList<ProtocolGlobalSettings> protocols,
        IReadOnlyList<EntityClient> clients) {
        if (string.IsNullOrEmpty(localConfiguration.Value.AwgPrivateKey))
            return;

        await nodeService.EnsureLocalAwgPublicKeyAsync();
        var allNodes = await nodeService.GetNodesAsync();
        var nodesWithAwg = allNodes.Where(n => n.AwgPublicKey != null).ToList();

        if (nodesWithAwg.Count < 2)
            return;

        var localNode = allNodes.FirstOrDefault(n => n.NodeId == localConfiguration.Value.NodeId);
        if (localNode == null)
            return;

        var globalConfig = await globalConfigurationService.GetAsync();
        var awgSettings = globalConfig.AwgSettings;

        var endpoint = new AwgEndpointConfig {
            Tag = "awg-mesh",
            UseIntegratedTun = options.Value.Awg.UseIntegratedTun,
            Address = [$"{localNode.AwgMeshIp}/24"],
            PrivateKey = localConfiguration.Value.AwgPrivateKey,
            ListenPort = awgSettings.ListenPort,
            Jc = awgSettings.Jc,
            Jmin = awgSettings.Jmin,
            Jmax = awgSettings.Jmax,
            S1 = awgSettings.S1,
            S2 = awgSettings.S2,
            H1 = awgSettings.H1.ToString(),
            H2 = awgSettings.H2.ToString(),
            H3 = awgSettings.H3.ToString(),
            H4 = awgSettings.H4.ToString()
        };

        foreach (var node in nodesWithAwg.Where(n => n.NodeId != localConfiguration.Value.NodeId)) {
            var peer = new WireGuardPeer {
                PublicKey = node.AwgPublicKey!,
                AllowedIps = [$"{node.AwgMeshIp}/32"],
                PersistentKeepaliveInterval = 25
            };

            var publicHost = await nodeNetworkService.GetPublicHostAsync(node);
            if (!string.IsNullOrEmpty(publicHost)) {
                peer.Address = HostAddress.Parse(publicHost).FormatForUri();

                peer.Port = awgSettings.ListenPort;
            }

            endpoint.Peers.Add(peer);
        }

        config.Endpoints.Add(endpoint);
    }
}