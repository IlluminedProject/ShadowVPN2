using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.SingBox.Contributors;

public sealed class WireGuardAmneziaConfigContributor(WireGuardKeyService keyService) : ISingBoxConfigContributor {
    public async Task ContributeAsync(SingBoxConfig config, IReadOnlyList<ProtocolGlobalSettings> protocols,
        IReadOnlyList<EntityClient> clients) {
        var settings = protocols.OfType<WireGuardAmneziaGlobalSettings>().FirstOrDefault(s => s.Enabled);
        if (settings is null) return;

        EndpointConfig endpoint;
        if (settings.IsAmneziaWg) {
            endpoint = new AwgEndpointConfig {
                Tag = "wireguard-clients",
                Mtu = settings.Mtu,
                Address = [settings.ServerAddress],
                PrivateKey = settings.PrivateKey,
                ListenPort = settings.ListenPort,
                Jc = settings.Jc,
                Jmin = settings.Jmin,
                Jmax = settings.Jmax,
                S1 = settings.S1,
                S2 = settings.S2,
                S3 = settings.S3,
                S4 = settings.S4,
                H1 = settings.H1,
                H2 = settings.H2,
                H3 = settings.H3,
                H4 = settings.H4,
                I1 = settings.I1,
                I2 = settings.I2,
                I3 = settings.I3,
                I4 = settings.I4,
                I5 = settings.I5
            };
        }
        else {
            endpoint = new WireGuardEndpointConfig {
                Tag = "wireguard-clients",
                Mtu = settings.Mtu,
                Address = [settings.ServerAddress],
                PrivateKey = settings.PrivateKey,
                ListenPort = settings.ListenPort
            };
        }

        var peers = endpoint is AwgEndpointConfig awgEndpoint
            ? awgEndpoint.Peers
            : ((WireGuardEndpointConfig)endpoint).Peers;

        foreach (var client in clients.Where(c => c.IsEnabled)) {
            await keyService.EnsureKeyAsync(client);
            peers.Add(new WireGuardPeer {
                PublicKey = client.WireGuard!.PublicKey!,
                AllowedIps = [$"{client.GetAssignedIp()}/32"]
            });
        }

        config.Endpoints.Add(endpoint);
    }
}