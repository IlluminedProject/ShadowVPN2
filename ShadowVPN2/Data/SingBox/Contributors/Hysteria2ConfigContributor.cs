using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.SingBox.Contributors;

public class Hysteria2ConfigContributor : ISingBoxConfigContributor
{
    public Task ContributeAsync(SingBoxConfig config, IReadOnlyList<ProtocolGlobalSettings> protocols,
        IReadOnlyList<EntityClient> clients)
    {
        var h2Instances = protocols.OfType<Hysteria2GlobalSettings>().Where(h => h.Enabled).ToList();
        if (h2Instances.Count == 0) return Task.CompletedTask;

        var users = clients
            .Select(c => new Hysteria2User
            {
                Name = c.Name,
                Password = c.Hysteria2?.Password ?? c.Id // Fallback to client ID if no password set
            })
            .ToList();

        foreach (var h2 in h2Instances)
        {
            var inbound = new Hysteria2InboundConfig
            {
                Tag = h2.Id ?? $"hysteria2-{h2.ListenPort}",
                Listen = "::",
                ListenPort = h2.ListenPort,
                Users = users,
                Obfs = h2.ObfsType != "none" && !string.IsNullOrWhiteSpace(h2.ObfsPassword)
                    ? new Hysteria2ObfsConfig
                    {
                        Type = h2.ObfsType,
                        Password = h2.ObfsPassword
                    }
                    : null,
                Tls = new InboundTlsConfig
                {
                    Enabled = true,
                    CertificatePath = h2.TlsCertificatePath,
                    KeyPath = h2.TlsKeyPath
                }
            };

            config.Inbounds.Add(inbound);
        }

        return Task.CompletedTask;
    }
}