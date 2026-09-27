using Microsoft.AspNetCore.Identity;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Contracts.Clients;
using ShadowVPN2.Data;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class ClientUpdatesService(
    ClientService clientService,
    FrontendUserContext userContext,
    UserManager<ApplicationUser> userManager) : IClientUpdatesService {
    public async Task<IAsyncDisposable> SubscribeAsync(Func<IReadOnlyList<ClientResponse>, Task> onUpdate,
        CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        var isAdministrator = await userContext.IsInRoleAsync(AppRoles.Administrator);
        var subscription = await clientService.SubscribeAsync(user, isAdministrator, async clients => {
            var ownerNames = await GetOwnerNamesAsync(clients);
            await onUpdate(clients.Select(client => ClientMapper.MapToResponse(client, user.Id,
                ownerNames.GetValueOrDefault(client.UserId))).ToArray());
        });
        return new Subscription(subscription);
    }

    private async Task<Dictionary<string, string>> GetOwnerNamesAsync(IReadOnlyList<EntityClient> clients) {
        var userIds = clients.Select(client => client.UserId).Distinct().ToArray();
        var result = new Dictionary<string, string>();
        foreach (var userId in userIds) {
            var owner = await userManager.FindByIdAsync(userId);
            result[userId] = owner?.UserName ?? owner?.Email ?? owner?.Id ?? "Unknown user";
        }

        return result;
    }

    private sealed class Subscription(ClientService.ClientSubscription subscription) : IAsyncDisposable {
        public ValueTask DisposeAsync() {
            subscription.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}