using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Data.Certificates;

namespace ShadowVPN2.Data.Subscription;

public sealed class Hysteria2SubscriptionContributor(ManagedCertificateService managedCertificateService)
    : ISubscriptionConnectionContributor {
    public string Protocol {
        get => "Hysteria2";
    }

    public Task<ProtocolConnectionInfo?> CreateAsync(
        EntityClient client,
        ProtocolGlobalSettings settings,
        string host,
        string sni,
        CancellationToken cancellationToken = default) {
        if (client.Hysteria2 is null)
            return Task.FromResult<ProtocolConnectionInfo?>(null);

        var h2 = settings as Hysteria2GlobalSettings
                 ?? throw new ArgumentException("Invalid Hysteria2 settings", nameof(settings));
        var password = client.Hysteria2.Password ?? client.Id;
        var fingerprint = managedCertificateService.GetCertificate(sni) == null
            ? h2.GetCertificateFingerprint()
            : null;
        return Task.FromResult<ProtocolConnectionInfo?>(new Hysteria2ConnectionInfo {
            ServerAddress = host,
            ServerPort = h2.ListenPort,
            Password = password,
            ObfsType = h2.ObfsType,
            ObfsPassword = h2.ObfsPassword,
            Sni = sni,
            PinSHA256 = fingerprint
        });
    }
}