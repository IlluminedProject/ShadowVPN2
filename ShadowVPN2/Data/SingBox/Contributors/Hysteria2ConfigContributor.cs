using ShadowVPN2.Data.SingBox.Models;
using Microsoft.Extensions.Options;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.SingBox.Contributors;

public class Hysteria2ConfigContributor(
    ManagedCertificateService managedCertificateService,
    IOptions<Infrastructure.Configurations.LocalConfiguration> localConfiguration) : ISingBoxConfigContributor {
    public Task ContributeAsync(SingBoxConfig config, IReadOnlyList<ProtocolGlobalSettings> protocols,
        IReadOnlyList<EntityClient> clients) {
        var h2Instances = protocols.OfType<Hysteria2GlobalSettings>().Where(h => h.Enabled).ToList();
        if (h2Instances.Count == 0) return Task.CompletedTask;

        var users = clients
            .Select(c => new Hysteria2User {
                Name = c.Name,
                Password = c.Hysteria2?.Password ?? c.Id // Fallback to client ID if no password set
            })
            .ToList();

        foreach (var h2 in h2Instances) {
            var managedCertificate = managedCertificateService.GetForNode(localConfiguration.Value.NodeId);
            var inbound = new Hysteria2InboundConfig {
                Tag = $"hysteria2-{h2.ListenPort}",
                Listen = "0.0.0.0",
                ListenPort = h2.ListenPort,
                Users = users,
                Obfs = h2.ObfsType != "none" && !string.IsNullOrWhiteSpace(h2.ObfsPassword)
                    ? new Hysteria2ObfsConfig {
                        Type = h2.ObfsType,
                        Password = h2.ObfsPassword
                    }
                    : null,
                Tls = new InboundTlsConfig {
                    Enabled = true,
                    Certificate = PemToLines(managedCertificate?.CertificatePem ?? h2.TlsCertificatePem),
                    Key = PemToLines(managedCertificate?.PrivateKeyPem ?? h2.TlsKeyPem)
                }
            };

            config.Inbounds.Add(inbound);
        }

        return Task.CompletedTask;
    }

    private static List<string> PemToLines(string pem) {
        return pem.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
    }
}