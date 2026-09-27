using ShadowVPN2.Contracts.Nodes;

namespace ShadowVPN2.Contracts.Application;

public interface INodeUpdatesService {
    Task<IAsyncDisposable> SubscribeAsync(Func<IReadOnlyList<NodeResponse>, Task> onUpdate,
        CancellationToken cancellationToken = default);
}