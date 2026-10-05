using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.Subscription;

public sealed class NaiveProxySubscriptionContributor(
    ManagedCertificateService managedCertificateService,
    GlobalConfigurationService globalConfigurationService,
    ClientService clientService) : ISubscriptionConnectionContributor {
    public string Protocol {
        get => "NaiveProxy";
    }

    public async Task<ProtocolConnectionInfo?> CreateAsync(EntityClient client, ProtocolGlobalSettings settings,
        string host, string sni, CancellationToken cancellationToken = default) {
        if (!client.IsEnabled) return null;

        var naiveSettings = settings as NaiveProxyGlobalSettings
                            ?? throw new ArgumentException("Invalid NaiveProxy settings", nameof(settings));
        var credentials = await clientService.EnsureNaiveProxyCredentialsAsync(client, cancellationToken);
        var configuredDomain = naiveSettings.MainDomain ??
                               (await globalConfigurationService.GetAsync(cancellationToken)).MainDomain;
        var tlsServerName = string.IsNullOrWhiteSpace(configuredDomain) ? sni : configuredDomain;
        var managedCertificate = managedCertificateService.GetCertificate(tlsServerName);
        var hasTrustedCertificate = managedCertificate is not null;
        var pin = hasTrustedCertificate
            ? null
            : naiveSettings.GetCertificateFingerprint()
              ?? throw new InvalidOperationException("NaiveProxy fallback certificate fingerprint is unavailable");
        return new NaiveProxyConnectionInfo {
            ServerAddress = host,
            ServerPort = naiveSettings.ListenPort,
            Username = credentials.Username,
            Password = credentials.Password,
            Sni = hasTrustedCertificate ? tlsServerName : "shadowvpn.local",
            Insecure = !hasTrustedCertificate,
            PinSha256 = pin
        };
    }
}