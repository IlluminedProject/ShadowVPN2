using System.Net;
using System.Net.Sockets;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.SingBox.Contributors;

public class AwgMeshConfigContributor(
    NodeService nodeService,
    LocalConfiguration localConfiguration,
    GlobalConfigurationService globalConfigurationService) : ISingBoxConfigContributor {
    public async Task ContributeAsync(SingBoxConfig config, IReadOnlyList<ProtocolGlobalSettings> protocols,
        IReadOnlyList<EntityClient> clients) {
        if (string.IsNullOrEmpty(localConfiguration.AwgPrivateKey))
            return;

        await nodeService.EnsureLocalAwgPublicKeyAsync();
        var allNodes = await nodeService.GetNodesAsync();
        var nodesWithAwg = allNodes.Where(n => n.AwgPublicKey != null).ToList();

        if (nodesWithAwg.Count < 2)
            return;

        var localNode = allNodes.FirstOrDefault(n => n.NodeId == localConfiguration.NodeId);
        if (localNode == null)
            return;

        var globalConfig = await globalConfigurationService.GetAsync();
        var awgSettings = globalConfig.AwgSettings;

        var endpoint = new AwgEndpointConfig {
            Tag = "awg-mesh",
            UseIntegratedTun = true,
            Address = [$"{localNode.AwgMeshIp}/24"],
            PrivateKey = localConfiguration.AwgPrivateKey,
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

        foreach (var node in nodesWithAwg.Where(n => n.NodeId != localConfiguration.NodeId)) {
            var peer = new WireGuardPeer {
                PublicKey = node.AwgPublicKey!,
                AllowedIps = [$"{node.AwgMeshIp}/32"],
                PersistentKeepaliveInterval = 25
            };

            if (!string.IsNullOrEmpty(node.Address)) {
                // Ensure proper formatting for IPv6 addresses
                if (IPAddress.TryParse(node.Address, out var ip)) {
                    if (ip.IsIPv4MappedToIPv6)
                        ip = ip.MapToIPv4();

                    peer.Address = ip.AddressFamily == AddressFamily.InterNetworkV6
                        ? $"[{ip}]"
                        : ip.ToString();
                }
                else
                    peer.Address = new AwgPeerInfo(string.Empty, string.Empty, node.Address).PublicHost;

                peer.Port = awgSettings.ListenPort;
            }

            endpoint.Peers.Add(peer);
        }

        config.Endpoints.Add(endpoint);
    }
}