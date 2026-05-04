using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.Subscription;

public class SubscriptionService(IAsyncDocumentSession session, NodeService nodeService)
{
    public async Task<SubscriptionResponse?> GetSubscriptionAsync(Guid subscriptionId)
    {
        var client = await session.Query<EntityClient>()
            .FirstOrDefaultAsync(c => c.SubscriptionId == subscriptionId);

        if (client is null) return null;

        var protocols = new List<ProtocolConnectionInfo>();
        var node = await nodeService.GetLocalNodeAsync();
        var address = node.Address; // The public IP or domain of the node

        if (client.Hysteria2 is not null)
        {
            var h2Global = await session.LoadAsync<Hysteria2GlobalSettings>("Global/Settings/Hysteria2")
                           ?? new Hysteria2GlobalSettings(); // Fallback if not set yet

            var password = $"{client.Name}:{client.Hysteria2.Password}";
            var port = h2Global.ListenPort;
            var obfsType = h2Global.ObfsType;
            var obfsPass = h2Global.ObfsPassword;
            var sni = address; // Use the address as SNI by default
            var fingerprint = h2Global.GetCertificateFingerprint();

            // Build the share URL (hysteria2://password@address:port/?obfs=type&obfs-password=pass&sni=sni&insecure=1&name=ClientName)
            var queryParams = new Dictionary<string, string?>
            {
                ["insecure"] = "1",
                ["pinSHA256"] = fingerprint,
                ["obfs"] = (string.IsNullOrEmpty(obfsType) || obfsType == "none") ? null : obfsType,
                ["obfs-password"] = (string.IsNullOrEmpty(obfsType) || obfsType == "none") ? null : obfsPass,
                ["sni"] = sni,
                ["name"] = client.Name
            };

            var queryString = string.Join("&", queryParams
                .Where(p => !string.IsNullOrEmpty(p.Value))
                .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

            var shareUrl = $"hysteria2://{password}@{address}:{port}/?{queryString}";

            protocols.Add(new Hysteria2ConnectionInfo
            {
                ServerAddress = address,
                ServerPort = port,
                Password = password,
                ObfsType = obfsType,
                ObfsPassword = obfsPass,
                Sni = sni,
                PinSHA256 = fingerprint,
                ShareUrl = shareUrl
            });
        }

        // Future protocols can be added here (e.g. WireGuard)

        return new SubscriptionResponse
        {
            ClientName = client.Name,
            Protocols = protocols.AsReadOnly()
        };
    }
}