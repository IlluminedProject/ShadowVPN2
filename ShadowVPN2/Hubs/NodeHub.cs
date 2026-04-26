using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ShadowVPN2.Data;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Hubs;

[Authorize(Policy = AppPermissions.Nodes.View)]
public class NodeHub(NodeService nodeService) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Create a subscription for this specific connection
        var subscription = await nodeService.SubscribeAsync(async nodes =>
        {
            // Push updates only to this caller
            await Clients.Caller.SendAsync("NodesUpdated", nodes);
        });

        // Store the subscription in Context.Items so it can be disposed in OnDisconnectedAsync
        Context.Items["NodeSubscription"] = subscription;

        // Send initial data immediately
        var initialNodes = await subscription.GetCurrentNodesAsync();
        await Clients.Caller.SendAsync("NodesUpdated", initialNodes);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Retrieve and dispose the subscription
        if (Context.Items.TryGetValue("NodeSubscription", out var sub) && sub is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await base.OnDisconnectedAsync(exception);
    }
}
