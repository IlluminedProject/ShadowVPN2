using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class VpnClientService(IAsyncDocumentSession session, ILogger<VpnClientService> logger)
{
    public async Task<IReadOnlyList<EntityVpnClient>> GetClientsAsync(ApplicationUser user,
        CancellationToken ct = default)
    {
        return await session.Query<EntityVpnClient>()
            .Where(c => c.UserId == user.Id)
            .ToListAsync(ct);
    }

    public async Task<EntityVpnClient?> GetClientAsync(string clientId, string userId,
        CancellationToken ct = default)
    {
        var client = await session.LoadAsync<EntityVpnClient>(clientId, ct);
        return client?.UserId == userId ? client : null;
    }

    public async Task<EntityVpnClient> AddClientAsync(ApplicationUser user, string name,
        WireGuardClientSettings? wireGuard = null, CancellationToken ct = default)
    {
        if (user.UserNumber == 0)
            throw new InvalidOperationException($"User {user.Id} has no UserNumber assigned");

        var client = new EntityVpnClient
        {
            Id = $"VpnClients/{user.UserNumber}|",
            UserId = user.Id!,
            Name = name,
            WireGuard = wireGuard,
        };

        await session.StoreAsync(client, ct);
        await session.SaveChangesAsync(ct);

        logger.LogInformation("VPN client {ClientId} created for user {UserId}", client.Id, user.Id);
        return client;
    }

    public async Task<EntityVpnClient?> UpdateClientAsync(string clientId, string userId, string name,
        WireGuardClientSettings? wireGuard, CancellationToken ct = default)
    {
        var client = await session.LoadAsync<EntityVpnClient>(clientId, ct);
        if (client is null || client.UserId != userId)
            return null;

        client.Name = name;
        client.WireGuard = wireGuard;

        await session.SaveChangesAsync(ct);
        return client;
    }

    public async Task<bool> DeleteClientAsync(string clientId, string userId, CancellationToken ct = default)
    {
        var client = await session.LoadAsync<EntityVpnClient>(clientId, ct);
        if (client is null || client.UserId != userId)
            return false;

        session.Delete(client);

        var stateId = clientId.Replace("VpnClients/", "VpnClientStates/");
        var state = await session.LoadAsync<EntityVpnClientState>(stateId, ct);
        if (state is not null)
            session.Delete(state);

        await session.SaveChangesAsync(ct);
        logger.LogInformation("VPN client {ClientId} deleted", clientId);
        return true;
    }
}
