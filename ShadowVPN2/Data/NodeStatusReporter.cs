using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Configurations;
using ShadowVPN2.Infrastructure.Containers.Registry.Models;

namespace ShadowVPN2.Data;

public sealed class NodeStatusReporter(
    IDocumentStore documentStore,
    IOptions<LocalConfiguration> localConfiguration,
    IEnumerable<INodeStatusCheck> checks,
    UpdaterClient updaterClient,
    ILogger<NodeStatusReporter> logger) : BackgroundService {
    private static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, NodeCheckResult> _results = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private NodeRuntimeMetadata _metadata = new();

    public static string GetStateId(Guid nodeId) {
        return $"NodeRuntimeStates/{nodeId:D}";
    }

    public async Task<IReadOnlyDictionary<Guid, EntityNodeRuntimeState>> GetStatesAsync(
        IEnumerable<Guid> nodeIds, CancellationToken cancellationToken = default) {
        var ids = nodeIds.Distinct().ToDictionary(GetStateId, nodeId => nodeId);
        if (ids.Count == 0)
            return new Dictionary<Guid, EntityNodeRuntimeState>();
        using var session = documentStore.OpenAsyncSession();
        var states = await session.LoadAsync<EntityNodeRuntimeState>(ids.Keys, cancellationToken);
        return states.Where(pair => pair.Value != null)
            .ToDictionary(pair => ids[pair.Key], pair => pair.Value!);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        await LoadMetadataAsync(stoppingToken);
        var registered = checks.ToDictionary(check => check.Id, StringComparer.Ordinal);
        var workers = registered.Values
            .Select(check => Task.Run(() => RunCheckAsync(check, stoppingToken), stoppingToken)).ToArray();
        try {
            using var timer = new PeriodicTimer(PublishInterval);
            do {
                await PublishAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally {
            await Task.WhenAll(workers);
        }
    }

    private async Task RunCheckAsync(INodeStatusCheck check, CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(PublishInterval);
        do {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(CheckTimeout);
            NodeCheckResult result;
            try {
                result = await check.CheckAsync(timeout.Token);
                if (timeout.IsCancellationRequested)
                    throw new OperationCanceledException(timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                return;
            }
            catch (Exception exception) {
                logger.LogWarning(exception, "Node check {CheckId} failed", check.Id);
                var now = DateTimeOffset.UtcNow;
                result = new NodeCheckResult {
                    Status = NodeCheckStatus.Unknown,
                    CheckedAt = now,
                    ValidUntil = now.Add(PublishInterval),
                    Message = exception is OperationCanceledException ? "Check timed out" : "Check failed"
                };
            }

            _results[check.Id] = result;
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task LoadMetadataAsync(CancellationToken stoppingToken) {
        try {
            var image = await updaterClient.GetImageInfoAsync(stoppingToken);
            var actual = ImageReference.Parse(image.ImageRepository ?? "");
            string? digest = null;
            foreach (var reference in image.RepoDigests) {
                var separator = reference.LastIndexOf('@');
                if (separator < 0 || !ImageDigest.IsValid(reference[(separator + 1)..]))
                    continue;
                var repository = ImageReference.Parse(reference[..separator]);
                if (repository.Registry != actual.Registry || repository.Repository != actual.Repository)
                    continue;
                digest = reference[(separator + 1)..];
                break;
            }

            _metadata = _metadata with { ContainerImageDigest = digest };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception exception) {
            logger.LogWarning(exception, "Failed to inspect node container image");
        }
    }

    private async Task PublishAsync(CancellationToken cancellationToken) {
        try {
            var nodeId = localConfiguration.Value.NodeId;
            var state = new EntityNodeRuntimeState {
                Id = GetStateId(nodeId), NodeId = nodeId, InstanceId = _instanceId,
                StartedAt = _startedAt, HeartbeatAt = DateTimeOffset.UtcNow,
                Metadata = _metadata, Checks = new Dictionary<string, NodeCheckResult>(_results.ToArray())
            };
            using var session = documentStore.OpenAsyncSession();
            await session.StoreAsync(state, state.Id, cancellationToken);
            await session.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        }
        catch (Exception exception) {
            logger.LogWarning(exception, "Failed to publish node runtime state");
        }
    }
}