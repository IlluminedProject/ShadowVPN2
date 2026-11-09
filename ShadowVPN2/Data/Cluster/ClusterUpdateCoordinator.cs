using System.Text.Json;
using System.Threading.Channels;
using Polly;
using Polly.Registry;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using Raven.Client.Http;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Updater;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Containers.Registry;
using ShadowVPN2.Infrastructure.Containers.Registry.Models;

namespace ShadowVPN2.Data.Cluster;

public sealed class ClusterUpdateCoordinator(
    ClusterUpdateService updateService,
    IDocumentStore documentStore,
    UpdaterClient updaterClient,
    RegistryImageClient registry,
    ResiliencePipelineProvider<string> resiliencePipelines,
    ILogger<ClusterUpdateCoordinator> logger) : BackgroundService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private (Guid UpdateId, string NodeTag)? _activeNode;
    private (Guid UpdateId, string NodeTag, string Error)? _pendingFailure;


    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await ObserveUpdatesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    break;
                }
                catch (Exception exception) {
                    logger.LogError(exception, "Cluster update change subscription failed; retrying");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        }
    }

    private async Task ObserveUpdatesAsync(CancellationToken stoppingToken) {
        var updates = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) {
            SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite
        });
        var changes = documentStore.Changes();
        var observable = changes.ForDocument(ClusterUpdateDocument.DocumentId);
        using var subscription = observable.Subscribe(new ActionObserver<DocumentChange>(
            _ => updates.Writer.TryWrite(true),
            error => updates.Writer.TryComplete(error)));

        void OnConnectionStatusChanged(object? sender, EventArgs args) {
            updates.Writer.TryWrite(true);
        }

        changes.ConnectionStatusChanged += OnConnectionStatusChanged;
        try {
            while (!stoppingToken.IsCancellationRequested) {
                while (updates.Reader.TryRead(out _)) { }

                var connection = changes.EnsureConnectedNow();
                var subscribed = observable.EnsureSubscribedNow();
                var subscriptionReady = connection.IsCompletedSuccessfully && subscribed.IsCompletedSuccessfully;

                var active = true;
                var context = ResilienceContextPool.Shared.Get("reconcile", stoppingToken);
                try {
                    active = await resiliencePipelines.GetPipeline("cluster-update").ExecuteAsync(async execution => {
                        try {
                            var running = await ReconcileAsync(execution.CancellationToken);
                            await updateService.ArchiveCompletedAsync(execution.CancellationToken);
                            return running;
                        }
                        catch (Exception exception) when (!stoppingToken.IsCancellationRequested) {
                            if (_activeNode is { } node && _pendingFailure is null)
                                _pendingFailure = (node.UpdateId, node.NodeTag, exception.GetBaseException().Message);
                            throw;
                        }
                    }, context);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    throw;
                }
                catch (Exception exception) {
                    logger.LogError(exception, "Cluster update reconciliation retry pipeline exhausted its attempts");
                }
                finally {
                    ResilienceContextPool.Shared.Return(context);
                }

                if (connection.IsCompleted)
                    await connection;
                if (subscribed.IsCompleted)
                    await subscribed;

                using var wakeup = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                if (active || !subscriptionReady || !changes.Connected)
                    wakeup.CancelAfter(TimeSpan.FromSeconds(5));
                try {
                    var notification = updates.Reader.WaitToReadAsync(wakeup.Token).AsTask();
                    if (!subscriptionReady) {
                        var readiness =
                            (connection.IsCompletedSuccessfully ? subscribed : connection).WaitAsync(wakeup.Token);
                        var completed = await Task.WhenAny(notification, readiness);
                        await completed;
                    }
                    else if (!await notification) {
                        return;
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
                    // Poll active operations and recover missed changes until the subscription is ready.
                }
                finally {
                    await wakeup.CancelAsync();
                }
            }
        }
        finally {
            changes.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }
    }

    private async Task<bool> ReconcileAsync(CancellationToken cancellationToken) {
        if (_pendingFailure is { } failure) {
            await updateService.RecordReconciliationFailureAsync(failure.UpdateId, failure.NodeTag,
                failure.Error, 5, cancellationToken);
            _pendingFailure = null;
        }

        var update = await updateService.GetAsync(cancellationToken);
        _activeNode = null;
        if (update is null)
            return false;
        var topology = await documentStore.Maintenance.Server.SendAsync(new GetClusterTopologyOperation(),
            cancellationToken);
        var localTag = topology.NodeTag;
        var localNode = update.Nodes.FirstOrDefault(node => node.NodeTag == localTag);
        var desiredDigest = update.DesiredImageDigest ?? update.ImageDigest;
        if (localNode?.Status == Contracts.Networking.ClusterUpdateNodeStatus.Skipped &&
            await ReconcileLocalDigestAsync(update, localTag, desiredDigest, cancellationToken))
            return true;
        if (update.Status is not (ClusterUpdateStatus.Preparing or ClusterUpdateStatus.Running))
            return false;
        _activeNode = (update.Status == ClusterUpdateStatus.Preparing &&
                       update.Nodes.Any(node =>
                           node.NodeTag == localTag &&
                           node.Status == Contracts.Networking.ClusterUpdateNodeStatus.Preparing)) ||
                      (update.Status == ClusterUpdateStatus.Running && update.CurrentNodeTag == localTag)
            ? (update.Id, localTag)
            : null;
        if (update.Status == ClusterUpdateStatus.Preparing) {
            if (localNode?.Status == Contracts.Networking.ClusterUpdateNodeStatus.Preparing) {
                logger.LogInformation(
                    "Cluster update preparing node {NodeTag}: update {UpdateId}, digest {ImageDigest}",
                    localTag, update.Id, update.ImageDigest);
                var prepared = await updaterClient.PrepareAsync(update.Id, update.ImageDigest, cancellationToken);
                logger.LogInformation(
                    "Cluster update prepare response for node {NodeTag}: update {UpdateId}, status {Status}, error {Error}",
                    localTag, update.Id, prepared.Status, prepared.Error);
                if (!Matches(update, prepared) ||
                    prepared.Status is not (UpdaterStatus.Prepared or UpdaterStatus.Succeeded or UpdaterStatus.Failed))
                    throw new InvalidOperationException(
                        $"Updater did not prepare update {update.Id}: {prepared.Status}; {prepared.Error}");
                await updateService.PrepareLocalAsync(localTag, update.Id,
                    prepared.Status is UpdaterStatus.Prepared or UpdaterStatus.Succeeded, prepared.Error,
                    cancellationToken);
            }

            return true;
        }

        if (update.Status != ClusterUpdateStatus.Running || update.CurrentNodeTag != localTag)
            return true;
        await ReconcileNodeAsync(update, localTag, topology, cancellationToken);
        return true;
    }

    private async Task<bool> ReconcileLocalDigestAsync(ClusterUpdateResponse update, string localTag,
        string desiredDigest, CancellationToken cancellationToken) {
        var local = await updaterClient.GetLocalStatusAsync(update.Id, desiredDigest, cancellationToken);
        if (local.Status == UpdaterStatus.Failed)
            throw new InvalidOperationException($"Updater could not inspect local image: {local.Error}");
        if (string.Equals(local.ImageDigest, desiredDigest, StringComparison.Ordinal)) {
            await updateService.CompleteCatchupAsync(localTag, update.Id, desiredDigest, cancellationToken);
            return false;
        }

        var status = await updaterClient.GetStatusAsync(update.Id, desiredDigest, cancellationToken);
        if (status.Status == UpdaterStatus.Running)
            return true;

        var image = local.ImageInfo;
        if (image?.CreatedAt is not { } currentCreated || string.IsNullOrEmpty(image.ImageRepository)) {
            logger.LogWarning("Skipping automatic catch-up on node {NodeTag}: current image build time is unknown",
                localTag);
            return false;
        }

        var targetCreated =
            await registry.GetCreatedAsync(ImageReference.Parse(image.ImageRepository), desiredDigest, image,
                cancellationToken);
        if (targetCreated is null || targetCreated <= currentCreated) {
            logger.LogInformation(
                "Skipping automatic catch-up on node {NodeTag}: target image is not known to be newer, current {CurrentCreated}, target {TargetCreated}, digest {ImageDigest}",
                localTag, currentCreated, targetCreated, desiredDigest);
            return false;
        }

        var prepared = await updaterClient.PrepareAsync(update.Id, desiredDigest, cancellationToken);
        if (prepared.UpdateId != update.Id || prepared.ImageDigest != desiredDigest ||
            prepared.Status is not (UpdaterStatus.Prepared or UpdaterStatus.Succeeded))
            throw new InvalidOperationException($"Updater could not prepare catch-up image: {prepared.Error}");
        var started = await updaterClient.StartAsync(update.Id, desiredDigest, cancellationToken);
        if (started.UpdateId == update.Id && started.ImageDigest == desiredDigest &&
            started.Status is UpdaterStatus.Running or UpdaterStatus.Succeeded)
            return true;
        throw new InvalidOperationException($"Updater could not start catch-up: {started.Error}");
    }

    private async Task ReconcileNodeAsync(ClusterUpdateResponse update, string localTag,
        ClusterTopologyResponse topology, CancellationToken cancellationToken) {
        var local = await updaterClient.GetLocalStatusAsync(update.Id, update.ImageDigest, cancellationToken);
        if (local.Status == UpdaterStatus.Failed)
            throw new InvalidOperationException($"Updater could not inspect local image: {local.Error}");
        if (string.Equals(local.ImageDigest, update.ImageDigest, StringComparison.Ordinal)) {
            await updateService.CompleteAlreadyUpdatedLocalAsync(localTag, update.Id, cancellationToken);
            return;
        }

        var status = await updaterClient.GetStatusAsync(update.Id, update.ImageDigest, cancellationToken);
        var ownsStep = update.Nodes.Any(node =>
            node.NodeTag == localTag && node.Status == Contracts.Networking.ClusterUpdateNodeStatus.Claimed);
        if (ownsStep && await CompleteAsync(update, localTag, status, cancellationToken))
            return;
        if (Matches(update, status) && status.Status == UpdaterStatus.Running)
            return;

        var claimed = await updateService.ClaimLocalAsync(localTag, topology, cancellationToken);
        if (claimed is null)
            return;

        // The cluster-wide claim may observe a newer rollout than the initial read.
        if (claimed.Id != update.Id || claimed.ImageDigest != update.ImageDigest)
            return;
        if (await CompleteAsync(claimed, localTag, status, cancellationToken))
            return;
        if (Matches(claimed, status) && status.Status != UpdaterStatus.Prepared)
            throw new InvalidOperationException($"Updater returned unexpected status {status.Status}");
        if (status.Status == UpdaterStatus.Running) {
            throw new InvalidOperationException($"Updater is running another operation {status.UpdateId}");
        }

        var started = await updaterClient.StartAsync(claimed.Id, claimed.ImageDigest, cancellationToken);
        if (await CompleteAsync(claimed, localTag, started, cancellationToken))
            return;
        if (Matches(claimed, started) && started.Status == UpdaterStatus.Running) {
            logger.LogInformation("Updater accepted update {UpdateId} for node {NodeTag} with digest {ImageDigest}",
                claimed.Id, localTag, claimed.ImageDigest);
        }
        else {
            throw new InvalidOperationException(
                $"Updater did not accept update {claimed.Id}: {started.Status}; {started.Error}");
        }
    }

    private async Task<bool> CompleteAsync(ClusterUpdateResponse update, string localTag, UpdaterResponse status,
        CancellationToken cancellationToken) {
        if (!Matches(update, status) || status.Status is not (UpdaterStatus.Succeeded or UpdaterStatus.Failed))
            return false;
        await updateService.CompleteLocalAsync(localTag, new CompleteClusterUpdateRequest {
            UpdateId = update.Id,
            ImageDigest = status.ImageDigest,
            Succeeded = status.Status == UpdaterStatus.Succeeded,
            Error = status.Error
        }, cancellationToken);
        logger.LogInformation("Recorded updater result {UpdaterStatus} for update {UpdateId} on node {NodeTag}",
            status.Status, update.Id, localTag);
        return true;
    }

    private static bool Matches(ClusterUpdateResponse update, UpdaterResponse status) {
        return update.Id == status.UpdateId &&
               string.Equals(update.ImageDigest, status.ImageDigest, StringComparison.Ordinal);
    }
}