using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ShadowVPN2.Data;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Hubs;

[Authorize(Policy = AppPermissions.Nodes.View)]
public class NodeHub(NodeService nodeService) : Hub
{
    private NodeService.NodeSubscription? _subscription;

    public override async Task OnConnectedAsync()
    {
        // Create a subscription for this specific connection
        _subscription = await nodeService.SubscribeAsync(async nodes =>
        {
            // Push updates only to this caller if needed,
            // though NodeService also pushes to All for convenience.
            // But having a per-caller subscription is more flexible for future filters.
            await Clients.Caller.SendAsync("NodesUpdated", nodes);
        });

        // Send initial data immediately
        var initialNodes = await _subscription.GetCurrentNodesAsync();
        await Clients.Caller.SendAsync("NodesUpdated", initialNodes);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _subscription?.Dispose();
        await base.OnDisconnectedAsync(exception);
    }
}
