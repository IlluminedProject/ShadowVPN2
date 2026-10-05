using Microsoft.Extensions.Options;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.SingBox.Contributors;

public sealed class NaiveProxyConfigContributor(
    ManagedCertificateService managedCertificateService,
    GlobalConfigurationService globalConfigurationService,
    IOptions<LocalConfiguration> localConfiguration) : ISingBoxConfigContributor {
    public async Task ContributeAsync(SingBoxConfig config, IReadOnlyList<ProtocolGlobalSettings> protocols,
        IReadOnlyList<EntityClient> clients) {
        var settingsList = protocols.OfType<NaiveProxyGlobalSettings>().Where(settings => settings.Enabled).ToArray();
        if (settingsList.Length == 0) return;

        var globalConfiguration = await globalConfigurationService.GetAsync();
        var managedCertificate = managedCertificateService.GetForNode(localConfiguration.Value.NodeId);
        foreach (var settings in settingsList) {
            var configuredDomain = settings.MainDomain ?? globalConfiguration.MainDomain;
            var certificateDomain = configuredDomain is not null &&
                                    managedCertificate?.Domains.Contains(configuredDomain,
                                        StringComparer.OrdinalIgnoreCase) == true
                ? configuredDomain
                : string.IsNullOrWhiteSpace(configuredDomain)
                    ? managedCertificate?.Domains.FirstOrDefault()
                    : null;

            string certificatePem;
            string privateKeyPem;
            string serverName;
            if (certificateDomain is not null && managedCertificate is not null) {
                certificatePem = managedCertificate.CertificatePem;
                privateKeyPem = managedCertificate.PrivateKeyPem;
                serverName = certificateDomain;
            }
            else {
                certificatePem = settings.TlsCertificatePem;
                privateKeyPem = settings.TlsKeyPem;
                serverName = "shadowvpn.local";
            }

            config.Inbounds.Add(new NaiveProxyInboundConfig {
                Tag = $"naive-{settings.ListenPort}",
                Network = "tcp",
                Listen = "0.0.0.0",
                ListenPort = settings.ListenPort,
                Users = clients.Where(client => client.IsEnabled && client.NaiveProxy is not null)
                    .Select(client => new NaiveProxyUser {
                        Username = client.NaiveProxy!.Username,
                        Password = client.NaiveProxy.Password
                    }).ToList(),
                Tls = new InboundTlsConfig {
                    Enabled = true,
                    ServerName = serverName,
                    Certificate = PemToLines(certificatePem),
                    Key = PemToLines(privateKeyPem)
                }
            });
        }
    }

    private static List<string> PemToLines(string pem) {
        return pem.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToList();
    }
}