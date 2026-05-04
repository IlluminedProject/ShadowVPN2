using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;
using ShadowVPN2.Infrastructure.Extensions;

namespace ShadowVPN2.Data;

public class NodeService(
    IDocumentStore documentStore,
    LocalConfiguration localConfiguration,
    ILogger<NodeService> logger) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly HashSet<NodeSubscription> _subscriptions = new();
    private IDisposable? _changesSubscription;
    private CancellationTokenSource? _disposalCts;

    public void Dispose()
    {
        lock (_lock)
        {
            _disposalCts?.Cancel();
            _disposalCts?.Dispose();
            _disposalCts = null;

            _changesSubscription?.Dispose();
            _changesSubscription = null;
            _subscriptions.Clear();
        }
    }

    public async Task<NodeSubscription> SubscribeAsync(Func<IReadOnlyList<NodeResponse>, Task>? onUpdate = null)
    {
        var subscription = new NodeSubscription(this);
        if (onUpdate != null)
        {
            subscription.NodesUpdated += onUpdate;
        }

        lock (_lock)
        {
            _subscriptions.Add(subscription);

            // Cancel any pending disposal
            if (_disposalCts != null)
            {
                logger.LogInformation("New subscription received while disposal was pending. Cancelling disposal.");
                _disposalCts.Cancel();
                _disposalCts.Dispose();
                _disposalCts = null;
            }

            if (_subscriptions.Count == 1 && _changesSubscription == null)
            {
                logger.LogInformation("First subscription created. Opening RavenDB Changes subscription");
                InitializeChangesSubscription();
            }
        }

        return await Task.FromResult(subscription);
    }

    private void Unsubscribe(NodeSubscription subscription)
    {
        lock (_lock)
        {
            if (_subscriptions.Remove(subscription) && _subscriptions.Count == 0)
            {
                if (_changesSubscription != null)
                {
                    // Instead of immediate disposal, start a cooldown to prevent flapping (e.g. during Blazor pre-rendering)
                    _disposalCts?.Cancel();
                    _disposalCts?.Dispose();
                    _disposalCts = new CancellationTokenSource();
                    var token = _disposalCts.Token;

                    logger.LogInformation(
                        "Last subscription removed. Starting 10s cooldown before closing RavenDB Changes subscription");

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), token);

                            lock (_lock)
                            {
                                if (!token.IsCancellationRequested && _subscriptions.Count == 0 &&
                                    _changesSubscription != null)
                                {
                                    logger.LogInformation("Cooldown finished. Closing RavenDB Changes subscription");
                                    _changesSubscription.Dispose();
                                    _changesSubscription = null;
                                }
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            // Expected when a new subscriber joins
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error during RavenDB changes subscription disposal cooldown");
                        }
                    }, CancellationToken.None);
                }
            }
        }
    }

    private void InitializeChangesSubscription()
    {
        try
        {
            _changesSubscription = documentStore.Changes()
                .ForDocumentsInCollection<EntityClusterNode>()
                .Subscribe(new ActionObserver<DocumentChange>(change =>
                {
                    logger.LogInformation("Nodes collection changed: {Type} for {Id}", change.Type, change.Id);
                    NotifyNodesChanged();
                }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize RavenDB changes subscription for nodes");
        }
    }

    private void NotifyNodesChanged()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var nodes = await GetNodesAsync();
                var response = nodes.Select(n => new NodeResponse
                {
                    Id = n.Id,
                    NodeId = n.NodeId,
                    Name = n.Name,
                    Address = n.Address,
                    Number = n.Number
                }).ToList().AsReadOnly();

                // Notify individual subscribers (SignalR Hubs and Blazor components)
                List<NodeSubscription> targets;
                lock (_lock)
                {
                    targets = _subscriptions.ToList();
                }

                await Task.WhenAll(targets.Select(sub => sub.NotifyAsync(response)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error notifying node changes");
            }
        });
    }

    public async Task<IReadOnlyList<EntityClusterNode>> GetNodesAsync()
    {
        using var session = documentStore.OpenAsyncSession();
        var nodes = await session.Query<EntityClusterNode>().ToListAsync();
        return nodes.AsReadOnly();
    }

    public async Task<EntityClusterNode> GetLocalNodeAsync()
    {
        using var session = documentStore.OpenAsyncSession();
        var node = await session.Query<EntityClusterNode>()
            .FirstOrDefaultAsync(n => n.NodeId == localConfiguration.NodeId);

        return node.OrThrowNotFound("Local node not found in database");
    }

    public class NodeSubscription(NodeService service) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                service.Unsubscribe(this);
                _disposed = true;
            }
        }

        public event Func<IReadOnlyList<NodeResponse>, Task>? NodesUpdated;

        public async Task<IReadOnlyList<NodeResponse>> GetCurrentNodesAsync()
        {
            var nodes = await service.GetNodesAsync();
            return nodes.Select(n => new NodeResponse
            {
                Id = n.Id,
                NodeId = n.NodeId,
                Name = n.Name,
                Address = n.Address,
                Number = n.Number
            }).ToList().AsReadOnly();
        }

        public async Task NotifyAsync(IReadOnlyList<NodeResponse> nodes)
        {
            if (!_disposed && NodesUpdated != null)
            {
                await NodesUpdated.Invoke(nodes);
            }
        }
    }
}