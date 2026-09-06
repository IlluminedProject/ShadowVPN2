using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;
using ShadowVPN2.Infrastructure.Extensions;
using SessionOptions = Raven.Client.Documents.Session.SessionOptions;

namespace ShadowVPN2.Data;

public class NodeService(
    IDocumentStore documentStore,
    IOptions<LocalConfiguration> localConfiguration,
    NodeNetworkService nodeNetworkService,
    DomainValidationService domainValidationService,
    ILogger<NodeService> logger) : IDisposable {
    private readonly Lock _lock = new();
    private readonly HashSet<NodeSubscription> _subscriptions = new();
    private IDisposable? _changesSubscription;
    private IDisposable? _networkChangesSubscription;
    private CancellationTokenSource? _disposalCts;

    public void Dispose() {
        lock (_lock) {
            _disposalCts?.Cancel();
            _disposalCts?.Dispose();
            _disposalCts = null;

            _changesSubscription?.Dispose();
            _changesSubscription = null;
            _networkChangesSubscription?.Dispose();
            _networkChangesSubscription = null;
            _subscriptions.Clear();
        }
    }

    public async Task<NodeSubscription> SubscribeAsync(Func<IReadOnlyList<NodeResponse>, Task>? onUpdate = null) {
        var subscription = new NodeSubscription(this);
        if (onUpdate != null) {
            subscription.NodesUpdated += onUpdate;
        }

        lock (_lock) {
            _subscriptions.Add(subscription);

            // Cancel any pending disposal
            if (_disposalCts != null) {
                logger.LogInformation("New subscription received while disposal was pending. Cancelling disposal.");
                _disposalCts.Cancel();
                _disposalCts.Dispose();
                _disposalCts = null;
            }

            if (_subscriptions.Count == 1 && _changesSubscription == null) {
                logger.LogInformation("First subscription created. Opening RavenDB Changes subscription");
                InitializeChangesSubscription();
            }
        }

        return await Task.FromResult(subscription);
    }

    private void Unsubscribe(NodeSubscription subscription) {
        lock (_lock) {
            if (_subscriptions.Remove(subscription) && _subscriptions.Count == 0) {
                if (_changesSubscription != null) {
                    // Instead of immediate disposal, start a cooldown to prevent flapping (e.g. during Blazor pre-rendering)
                    _disposalCts?.Cancel();
                    _disposalCts?.Dispose();
                    _disposalCts = new CancellationTokenSource();
                    var token = _disposalCts.Token;

                    logger.LogInformation(
                        "Last subscription removed. Starting 10s cooldown before closing RavenDB Changes subscription");

                    _ = Task.Run(async () => {
                        try {
                            await Task.Delay(TimeSpan.FromSeconds(10), token);

                            lock (_lock) {
                                if (!token.IsCancellationRequested && _subscriptions.Count == 0 &&
                                    _changesSubscription != null) {
                                    logger.LogInformation("Cooldown finished. Closing RavenDB Changes subscription");
                                    _changesSubscription.Dispose();
                                    _changesSubscription = null;
                                    _networkChangesSubscription?.Dispose();
                                    _networkChangesSubscription = null;
                                }
                            }
                        }
                        catch (TaskCanceledException) {
                            // Expected when a new subscriber joins
                        }
                        catch (Exception ex) {
                            logger.LogError(ex, "Error during RavenDB changes subscription disposal cooldown");
                        }
                    }, CancellationToken.None);
                }
            }
        }
    }

    private void InitializeChangesSubscription() {
        try {
            _changesSubscription = documentStore.Changes()
                .ForDocumentsInCollection<EntityClusterNode>()
                .Subscribe(new ActionObserver<DocumentChange>(change => {
                    logger.LogInformation("Nodes collection changed: {Type} for {Id}", change.Type, change.Id);
                    NotifyNodesChanged();
                }));
            _networkChangesSubscription = documentStore.Changes()
                .ForDocumentsInCollection<EntityNodeNetworkStatus>()
                .Subscribe(new ActionObserver<DocumentChange>(change => {
                    logger.LogInformation("Node network status changed: {Type} for {Id}", change.Type, change.Id);
                    NotifyNodesChanged();
                }));
        }
        catch (Exception ex) {
            logger.LogError(ex, "Failed to initialize RavenDB changes subscription for nodes");
        }
    }

    private void NotifyNodesChanged() {
        _ = Task.Run(async () => {
            try {
                var nodes = await GetNodesAsync();
                var response = await CreateResponsesAsync(nodes);

                // Notify individual subscribers (SignalR Hubs and Blazor components)
                List<NodeSubscription> targets;
                lock (_lock) {
                    targets = _subscriptions.ToList();
                }

                await Task.WhenAll(targets.Select(sub => sub.NotifyAsync(response)));
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error notifying node changes");
            }
        });
    }

    public async Task<IReadOnlyList<EntityClusterNode>> GetNodesAsync() {
        using var session = documentStore.OpenAsyncSession(new SessionOptions());
        var nodes = await session.Query<EntityClusterNode>().ToListAsync();
        return nodes.AsReadOnly();
    }

    public async Task<IReadOnlyList<NodeResponse>> GetNodeResponsesAsync(
        CancellationToken cancellationToken = default) {
        var nodes = await GetNodesAsync();
        return await CreateResponsesAsync(nodes, cancellationToken);
    }

    public async Task<NodeResponse> UpdateDomainAsync(Guid nodeId, string? domain,
        CancellationToken cancellationToken = default) {
        var normalizedDomain = domainValidationService.Normalize(domain);
        using var session = documentStore.OpenAsyncSession();
        if (normalizedDomain != null && await session.Query<EntityClusterNode>()
                .AnyAsync(candidate => candidate.NodeId != nodeId && candidate.Domain == normalizedDomain,
                    cancellationToken))
            throw new ArgumentException("Domain is already assigned to another node.");

        var node = await session.Query<EntityClusterNode>()
            .FirstOrDefaultAsync(candidate => candidate.NodeId == nodeId, cancellationToken);
        node = node.OrThrowNotFound("Node not found");
        node.Domain = normalizedDomain;
        await session.SaveChangesAsync(cancellationToken);

        return (await CreateResponsesAsync([node], cancellationToken))[0];
    }

    public async Task<DomainCheckResponse> CheckDomainAsync(Guid nodeId,
        CancellationToken cancellationToken = default) {
        using var session = documentStore.OpenAsyncSession();
        var node = await session.Query<EntityClusterNode>()
            .FirstOrDefaultAsync(candidate => candidate.NodeId == nodeId, cancellationToken);
        node = node.OrThrowNotFound("Node not found");
        var status = await nodeNetworkService.GetStatusAsync(nodeId, cancellationToken);
        return await domainValidationService.CheckAsync(node.Domain, status?.PublicIpv4, status?.PublicIpv6,
            cancellationToken);
    }

    public async Task EnsureLocalAwgPublicKeyAsync() {
        if (string.IsNullOrEmpty(localConfiguration.Value.AwgPrivateKey))
            return;

        var publicKey = AwgKeyGenerator.GetPublicKey(localConfiguration.Value.AwgPrivateKey);
        using var session = documentStore.OpenAsyncSession();
        var localNode = await session.Query<EntityClusterNode>()
            .FirstOrDefaultAsync(n => n.NodeId == localConfiguration.Value.NodeId);

        if (localNode == null || localNode.AwgPublicKey == publicKey)
            return;

        logger.LogWarning("Correcting local AWG public key in cluster state");
        localNode.AwgPublicKey = publicKey;
        await session.SaveChangesAsync();
    }

    public async Task<EntityClusterNode> GetLocalNodeAsync() {
        using var session = documentStore.OpenAsyncSession();
        var node = await session.Query<EntityClusterNode>()
            .FirstOrDefaultAsync(n => n.NodeId == localConfiguration.Value.NodeId);

        return node.OrThrowNotFound("Local node not found in database");
    }

    private async Task<IReadOnlyList<NodeResponse>> CreateResponsesAsync(
        IReadOnlyList<EntityClusterNode> nodes,
        CancellationToken cancellationToken = default) {
        var statuses = await nodeNetworkService.GetStatusesAsync(nodes.Select(node => node.NodeId), cancellationToken);
        var responses = await Task.WhenAll(nodes.Select(async node => {
            statuses.TryGetValue(node.NodeId, out var status);
            return new NodeResponse {
                Id = node.Id,
                NodeId = node.NodeId,
                Name = node.Name,
                Domain = node.Domain,
                PublicIpv4 = status?.PublicIpv4,
                PublicIpv6 = status?.PublicIpv6,
                InternalAddresses = status?.InternalAddresses.AsReadOnly() ?? [],
                NetworkCheckedAt = status?.CheckedAt,
                DomainStatus = await domainValidationService.CheckAsync(node.Domain, status?.PublicIpv4,
                    status?.PublicIpv6, cancellationToken),
                Number = node.Number,
                AwgPublicKey = node.AwgPublicKey,
                AwgMeshIp = node.AwgMeshIp,
                IsPending = node.JoinSecret.HasValue
            };
        }));
        return responses.ToList().AsReadOnly();
    }

    public class NodeSubscription(NodeService service) : IDisposable {
        private bool _disposed;

        public void Dispose() {
            if (!_disposed) {
                service.Unsubscribe(this);
                _disposed = true;
            }
        }

        public event Func<IReadOnlyList<NodeResponse>, Task>? NodesUpdated;

        public async Task<IReadOnlyList<NodeResponse>> GetCurrentNodesAsync() {
            var nodes = await service.GetNodesAsync();
            return await service.CreateResponsesAsync(nodes);
        }

        public async Task NotifyAsync(IReadOnlyList<NodeResponse> nodes) {
            if (!_disposed && NodesUpdated != null) {
                await NodesUpdated.Invoke(nodes);
            }
        }
    }
}