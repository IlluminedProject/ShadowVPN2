using System.Text;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure;

namespace ShadowVPN2.Data.Subscription;

public sealed class WireGuardSubscriptionContributor(
    WireGuardKeyService keyService) : ISubscriptionConnectionContributor {
    public string Protocol {
        get => "WireGuard";
    }

    public async Task<ProtocolConnectionInfo?> CreateAsync(EntityClient client, ProtocolGlobalSettings settings,
        string host, string sni, CancellationToken cancellationToken = default) {
        if (settings is not WireGuardAmneziaGlobalSettings wg || !client.IsEnabled)
            return null;

        await keyService.EnsureKeyAsync(client, cancellationToken);
        var serverPublicKey = AwgKeyGenerator.GetPublicKey(wg.PrivateKey);
        var mtu = client.WireGuard?.Mtu ?? wg.Mtu;
        var config =
            $"[Interface]\nPrivateKey = {client.WireGuard!.PrivateKey}\nAddress = {client.GetAssignedIp()}/32\nMTU = {mtu}\n\n[Peer]\nPublicKey = {serverPublicKey}\nEndpoint = {EndpointAddress.FormatHostForUri(host)}:{wg.ListenPort}\nAllowedIPs = 0.0.0.0/0, ::/0\nPersistentKeepalive = 25\n";

        if (wg.IsAmneziaWg) {
            config +=
                $"Jc = {wg.Jc}\nJmin = {wg.Jmin}\nJmax = {wg.Jmax}\nS1 = {wg.S1}\nS2 = {wg.S2}\nS3 = {wg.S3}\nS4 = {wg.S4}\nH1 = {wg.H1}\nH2 = {wg.H2}\nH3 = {wg.H3}\nH4 = {wg.H4}\n";
            config += Optional("I1", wg.I1) + Optional("I2", wg.I2) + Optional("I3", wg.I3) + Optional("I4", wg.I4) +
                      Optional("I5", wg.I5);
        }

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(config));
        return new WireGuardConnectionInfo {
            ServerAddress = host,
            ServerPort = wg.ListenPort,
            Config = config,
            ShareUrl = $"wireguard://{encoded}",
            IsAmneziaWg = wg.IsAmneziaWg
        };
    }

    private static string Optional(string name, string? value) {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{name} = {value}\n";
    }
}