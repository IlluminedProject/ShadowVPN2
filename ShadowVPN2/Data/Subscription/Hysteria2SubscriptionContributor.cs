using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.Subscription;

public sealed class Hysteria2SubscriptionContributor : ISubscriptionConnectionContributor {
    public string Protocol {
        get => "Hysteria2";
    }

    public Task<ProtocolConnectionInfo?> CreateAsync(
        EntityClient client,
        ProtocolGlobalSettings settings,
        SubscriptionEndpointContext endpoint,
        CancellationToken cancellationToken = default) {
        if (client.Hysteria2 is null)
            return Task.FromResult<ProtocolConnectionInfo?>(null);

        var h2 = settings as Hysteria2GlobalSettings
                 ?? throw new ArgumentException("Invalid Hysteria2 settings", nameof(settings));
        var password = client.Hysteria2.Password ?? client.Id;
        var fingerprint = h2.GetCertificateFingerprint();
        var queryParams = new Dictionary<string, string?> {
            ["insecure"] = "1",
            ["pinSHA256"] = fingerprint,
            ["obfs"] = h2.ObfsType is null or "none" ? null : h2.ObfsType,
            ["obfs-password"] = h2.ObfsType is null or "none" ? null : h2.ObfsPassword,
            ["sni"] = endpoint.Sni,
            ["name"] = client.Name
        };

        var queryString = string.Join("&", queryParams
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        return Task.FromResult<ProtocolConnectionInfo?>(new Hysteria2ConnectionInfo {
            ServerAddress = endpoint.Host,
            ServerPort = h2.ListenPort,
            Password = password,
            ObfsType = h2.ObfsType,
            ObfsPassword = h2.ObfsPassword,
            Sni = endpoint.Sni,
            PinSHA256 = fingerprint,
            ShareUrl =
                $"hysteria2://{Uri.EscapeDataString(password)}@{EndpointAddress.FormatHostForUri(endpoint.Host)}:{h2.ListenPort}/?{queryString}"
        });
    }
}