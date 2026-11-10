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
        var settingsList = protocols.Where(settings => settings.Enabled &&
                                                       settings is NaiveProxyGlobalSettings
                                                           or NaiveProxyQuicGlobalSettings).ToArray();
        if (settingsList.Length == 0) return;

        var globalConfiguration = await globalConfigurationService.GetAsync();
        var nodeCertificates = managedCertificateService.GetForNodeDomains(localConfiguration.Value.NodeId);
        foreach (var settings in settingsList) {
            var (fallbackCertificatePem, fallbackKeyPem) = settings switch {
                NaiveProxyGlobalSettings naive => (naive.TlsCertificatePem, naive.TlsKeyPem),
                NaiveProxyQuicGlobalSettings quic => (quic.TlsCertificatePem, quic.TlsKeyPem),
                _ => throw new InvalidOperationException($"Unsupported NaiveProxy settings {settings.GetType().Name}")
            };
            var quicEnabled = settings is NaiveProxyQuicGlobalSettings;
            var configuredDomain = settings.MainDomain ?? globalConfiguration.MainDomain;
            var certificates = nodeCertificates.Select(material => new InboundTlsCertificateConfig {
                Certificate = PemToLines(material.CertificatePem),
                Key = PemToLines(material.PrivateKeyPem)
            }).ToList();
            if (certificates.Count == 0) {
                certificates.Add(new InboundTlsCertificateConfig {
                    Certificate = PemToLines(fallbackCertificatePem),
                    Key = PemToLines(fallbackKeyPem)
                });
            }

            var serverName = configuredDomain?.ToString() ?? nodeCertificates.FirstOrDefault()?.Domains[0].ToString() ??
                "shadowvpn.local";

            config.Inbounds.Add(new NaiveProxyInboundConfig {
                Tag = $"{(quicEnabled ? "naive-quic" : "naive")}-{settings.ListenPort.ListenPort}",
                Network = quicEnabled ? "udp" : "tcp",
                Listen = "0.0.0.0",
                ListenPort = settings.ListenPort.ListenPort,
                Users = clients.Where(client => client.IsEnabled && client.NaiveProxy is not null)
                    .Select(client => new NaiveProxyUser {
                        Username = client.NaiveProxy!.Username,
                        Password = client.NaiveProxy.Password
                    }).ToList(),
                Tls = new InboundTlsConfig {
                    Enabled = true,
                    ServerName = serverName,
                    Certificates = certificates
                }
            });
        }
    }

    private static List<string> PemToLines(string pem) {
        return pem.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToList();
    }
}