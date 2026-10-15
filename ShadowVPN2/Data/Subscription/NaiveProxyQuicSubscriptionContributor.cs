using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.Subscription;

public sealed class NaiveProxyQuicSubscriptionContributor(
    ManagedCertificateService managedCertificateService,
    GlobalConfigurationService globalConfigurationService,
    ClientService clientService) : ISubscriptionConnectionContributor {
    private static readonly HostName FallbackServerName = HostName.Parse("shadowvpn.local");

    public string Protocol {
        get => "NaiveProxyQuic";
    }

    public async Task<ProtocolConnectionInfo?> CreateAsync(EntityClient client, ProtocolGlobalSettings settings,
        HostName host, HostName sni, CancellationToken cancellationToken = default) {
        if (!client.IsEnabled) return null;

        var naiveSettings = settings as NaiveProxyQuicGlobalSettings
                            ?? throw new ArgumentException("Invalid NaiveProxy QUIC settings", nameof(settings));
        var credentials = await clientService.EnsureNaiveProxyCredentialsAsync(client, cancellationToken);
        var configuredDomain = naiveSettings.MainDomain ??
                               (await globalConfigurationService.GetAsync(cancellationToken)).MainDomain;
        var tlsServerName = configuredDomain ?? sni;
        var managedCertificate = managedCertificateService.GetCertificate(tlsServerName);
        var hasTrustedCertificate = managedCertificate is not null;
        var pin = hasTrustedCertificate
            ? null
            : naiveSettings.GetCertificateFingerprint()
              ?? throw new InvalidOperationException("NaiveProxy QUIC fallback certificate fingerprint is unavailable");
        return new NaiveProxyConnectionInfo {
            ServerAddress = host,
            ServerPort = naiveSettings.ListenPort.ListenPort,
            Quic = true,
            Username = credentials.Username,
            Password = credentials.Password,
            Sni = hasTrustedCertificate ? tlsServerName : FallbackServerName,
            Insecure = !hasTrustedCertificate,
            PinSha256 = pin
        };
    }
}