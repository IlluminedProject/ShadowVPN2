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
        return new WireGuardConnectionInfo {
            ServerAddress = host,
            ServerPort = wg.ListenPort,
            PrivateKey = client.WireGuard!.PrivateKey!,
            AssignedIp = client.GetAssignedIp().ToString(),
            ServerPublicKey = serverPublicKey,
            Mtu = mtu,
            IsAmneziaWg = wg.IsAmneziaWg, Jc = wg.Jc,
            Jmin = wg.Jmin,
            Jmax = wg.Jmax,
            S1 = wg.S1,
            S2 = wg.S2,
            S3 = wg.S3,
            S4 = wg.S4,
            H1 = wg.H1,
            H2 = wg.H2,
            H3 = wg.H3,
            H4 = wg.H4,
            I1 = wg.I1,
            I2 = wg.I2,
            I3 = wg.I3,
            I4 = wg.I4,
            I5 = wg.I5
        };
    }
}