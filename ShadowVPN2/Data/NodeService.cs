using Microsoft.AspNetCore.SignalR;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class NodeService(IDocumentStore documentStore, ILogger<NodeService> logger) : IDisposable
{
    private IDisposable? _changesSubscription;
    private readonly HashSet<NodeSubscription> _subscriptions = new();
    private readonly Lock _lock = new();

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
            if (_subscriptions.Count == 1)
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
                    logger.LogInformation("Last subscription removed. Closing RavenDB Changes subscription");
                    _changesSubscription.Dispose();
                    _changesSubscription = null;
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

    public void Dispose()
    {
        lock (_lock)
        {
            _changesSubscription?.Dispose();
            _changesSubscription = null;
            _subscriptions.Clear();
        }
    }

    public class NodeSubscription(NodeService service) : IDisposable
    {
        private bool _disposed;

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

        public void Dispose()
        {
            if (!_disposed)
            {
                service.Unsubscribe(this);
                _disposed = true;
            }
        }
    }
}
