using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Data;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class NodeUpdatesService(
    NodeService nodeService,
    FrontendUserContext userContext) : INodeUpdatesService {
    public async Task<IAsyncDisposable> SubscribeAsync(Func<IReadOnlyList<NodeResponse>, Task> onUpdate,
        CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Nodes.View, cancellationToken);
        var subscription = await nodeService.SubscribeAsync(onUpdate);
        return new Subscription(subscription);
    }

    private sealed class Subscription(NodeService.NodeSubscription subscription) : IAsyncDisposable {
        public ValueTask DisposeAsync() {
            subscription.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}