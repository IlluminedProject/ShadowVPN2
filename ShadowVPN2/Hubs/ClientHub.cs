using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using ShadowVPN2.Data;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Extensions;

namespace ShadowVPN2.Hubs;

[Authorize]
public class ClientHub(ClientService clientService, UserManager<ApplicationUser> userManager) : Hub {
    public override async Task OnConnectedAsync() {
        var user = await userManager.GetRequiredUserAsync(Context.User!);
        var isAdministrator = Context.User!.IsInRole(AppRoles.Administrator);

        // Create a subscription for this specific connection
        var subscription = await clientService.SubscribeAsync(user, isAdministrator, async clients => {
            // Push updates only to this caller
            await Clients.Caller.SendAsync("ClientsUpdated", clients.Select(client =>
                ClientMapper.MapToResponse(client, user.Id)).ToList());
        });

        // Store the subscription in Context.Items so it can be disposed in OnDisconnectedAsync
        Context.Items["ClientSubscription"] = subscription;

        // Send initial data immediately
        var initialClients = await subscription.GetCurrentClientsAsync();
        await Clients.Caller.SendAsync("ClientsUpdated", initialClients.Select(client =>
            ClientMapper.MapToResponse(client, user.Id)).ToList());

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception) {
        // Retrieve and dispose the subscription
        if (Context.Items.TryGetValue("ClientSubscription", out var sub) && sub is IDisposable disposable) {
            disposable.Dispose();
        }

        await base.OnDisconnectedAsync(exception);
    }
}