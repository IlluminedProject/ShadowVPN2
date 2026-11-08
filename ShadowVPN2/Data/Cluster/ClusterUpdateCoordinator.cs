using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using Raven.Client.Http;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.Cluster;

public sealed class ClusterUpdateCoordinator(
    ClusterUpdateService updateService,
    IDocumentStore documentStore,
    IOptions<UpdaterOptions> options,
    ResiliencePipelineProvider<string> resiliencePipelines,
    ILogger<ClusterUpdateCoordinator> logger) : BackgroundService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SocketOperationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PrepareOperationTimeout = TimeSpan.FromMinutes(15);
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
        if (update.Status is not ("Preparing" or "Running"))
            return false;
        _activeNode = (update.Status == "Preparing" &&
                       update.Nodes.Any(node =>
                           node.NodeTag == localTag &&
                           node.Status == Contracts.Networking.ClusterUpdateNodeStatus.Preparing)) ||
                      (update.Status == "Running" && update.CurrentNodeTag == localTag)
            ? (update.Id, localTag)
            : null;
        if (update.Status == "Preparing") {
            if (localNode?.Status == Contracts.Networking.ClusterUpdateNodeStatus.Preparing) {
                logger.LogInformation(
                    "Cluster update preparing node {NodeTag}: update {UpdateId}, digest {ImageDigest}",
                    localTag, update.Id, update.ImageDigest);
                var prepared = await SendAsync("prepare", update, cancellationToken);
                logger.LogInformation(
                    "Cluster update prepare response for node {NodeTag}: update {UpdateId}, status {Status}, error {Error}",
                    localTag, update.Id, prepared.Status, prepared.Error);
                if (!Matches(update, prepared) || prepared.Status is not ("Prepared" or "Succeeded" or "Failed"))
                    throw new InvalidOperationException(
                        $"Updater did not prepare update {update.Id}: {prepared.Status}; {prepared.Error}");
                await updateService.PrepareLocalAsync(localTag, update.Id,
                    prepared.Status is "Prepared" or "Succeeded", prepared.Error, cancellationToken);
            }

            return true;
        }

        if (update.Status != "Running" || update.CurrentNodeTag != localTag)
            return true;
        await ReconcileNodeAsync(update, localTag, topology, cancellationToken);
        return true;
    }

    private async Task<bool> ReconcileLocalDigestAsync(ClusterUpdateResponse update, string localTag,
        string desiredDigest, CancellationToken cancellationToken) {
        var local = await SendAsync("local-status", CreateRequest(update, desiredDigest, update.Id), cancellationToken);
        if (local.Status == "Failed")
            throw new InvalidOperationException($"Updater could not inspect local image: {local.Error}");
        if (string.Equals(local.ImageDigest, desiredDigest, StringComparison.Ordinal))
            return false;

        var catchup = CreateRequest(update, desiredDigest, Guid.NewGuid());
        var status = await SendAsync("status", catchup, cancellationToken);
        if (Matches(catchup, status) && status.Status == "Running")
            return true;
        if (Matches(catchup, status) && status.Status == "Prepared")
            return await StartCatchupAsync(catchup, cancellationToken);

        var prepared = await SendAsync("prepare", catchup, cancellationToken);
        if (prepared.Status is "Prepared" or "Succeeded")
            return await StartCatchupAsync(catchup, cancellationToken);
        throw new InvalidOperationException($"Updater could not prepare catch-up image: {prepared.Error}");
    }

    private static ClusterUpdateResponse CreateRequest(ClusterUpdateResponse source, string imageDigest, Guid id) {
        return new ClusterUpdateResponse {
            Id = id, ImageDigest = imageDigest, DesiredImageDigest = imageDigest,
            Status = source.Status, Nodes = source.Nodes, CurrentNodeTag = source.CurrentNodeTag,
            Error = source.Error
        };
    }

    private async Task<bool> StartCatchupAsync(ClusterUpdateResponse catchup,
        CancellationToken cancellationToken) {
        var started = await SendAsync("start", catchup, cancellationToken);
        if (started.Status is "Running") return true;
        if (started.Status is "Succeeded" or "Failed")
            return false;
        throw new InvalidOperationException($"Updater could not start catch-up: {started.Error}");
    }

    private async Task ReconcileNodeAsync(ClusterUpdateResponse update, string localTag,
        ClusterTopologyResponse topology, CancellationToken cancellationToken) {
        var local = await SendAsync("local-status", update, cancellationToken);
        if (local.Status == "Failed")
            throw new InvalidOperationException($"Updater could not inspect local image: {local.Error}");
        if (string.Equals(local.ImageDigest, update.ImageDigest, StringComparison.Ordinal)) {
            await updateService.CompleteAlreadyUpdatedLocalAsync(localTag, update.Id, cancellationToken);
            return;
        }

        var status = await SendAsync("status", update, cancellationToken);
        var ownsStep = update.Nodes.Any(node =>
            node.NodeTag == localTag && node.Status == Contracts.Networking.ClusterUpdateNodeStatus.Claimed);
        if (ownsStep && await CompleteAsync(update, localTag, status, cancellationToken))
            return;
        if (Matches(update, status) && status.Status == "Running")
            return;

        var claimed = await updateService.ClaimLocalAsync(localTag, topology, cancellationToken);
        if (claimed is null)
            throw new InvalidOperationException("Cannot claim update step: RavenDB restart quorum is unavailable");

        // The cluster-wide claim may observe a newer rollout than the initial read.
        if (claimed.Id != update.Id || claimed.ImageDigest != update.ImageDigest)
            return;
        if (await CompleteAsync(claimed, localTag, status, cancellationToken))
            return;
        if (Matches(claimed, status) && status.Status != "Prepared")
            throw new InvalidOperationException($"Updater returned unexpected status {status.Status}");
        if (status.Status == "Running") {
            throw new InvalidOperationException($"Updater is running another operation {status.UpdateId}");
        }

        var started = await SendAsync("start", claimed, cancellationToken);
        if (await CompleteAsync(claimed, localTag, started, cancellationToken))
            return;
        if (Matches(claimed, started) && started.Status == "Running") {
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
        if (!Matches(update, status) || status.Status is not ("Succeeded" or "Failed"))
            return false;
        await updateService.CompleteLocalAsync(localTag, new CompleteClusterUpdateRequest {
            UpdateId = update.Id,
            ImageDigest = status.ImageDigest,
            Succeeded = status.Status == "Succeeded",
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

    private async Task<UpdaterResponse> SendAsync(string operation, ClusterUpdateResponse update,
        CancellationToken cancellationToken) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(operation == "prepare" ? PrepareOperationTimeout : SocketOperationTimeout);
        logger.LogDebug(
            "Sending updater operation {Operation}: update {UpdateId}, digest {ImageDigest}, timeout {Timeout}",
            operation, update.Id, update.ImageDigest,
            timeout.Token.CanBeCanceled
                ? operation == "prepare" ? PrepareOperationTimeout : SocketOperationTimeout
                : TimeSpan.Zero);
        var stopwatch = Stopwatch.StartNew();
        try {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(options.Value.SocketPath), timeout.Token);
            await using var stream = new NetworkStream(socket, false);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var request =
                JsonSerializer.Serialize(new UpdaterRequest(operation, update.Id, update.ImageDigest), JsonOptions);
            await writer.WriteLineAsync(request.AsMemory(), timeout.Token);
            await writer.FlushAsync(timeout.Token);
            var response = await reader.ReadLineAsync(timeout.Token);
            var result = response is not null
                ? JsonSerializer.Deserialize<UpdaterResponse>(response, JsonOptions) ??
                  throw new IOException("Updater returned an empty JSON response")
                : throw new IOException("Updater closed the socket without a response");
            logger.LogDebug(
                "Updater operation completed: {Operation}, update {UpdateId}, status {Status}, elapsed {ElapsedMilliseconds} ms",
                operation, update.Id, result.Status, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception exception) {
            logger.LogError(exception,
                "Updater operation failed: {Operation}, update {UpdateId}, digest {ImageDigest}, elapsed {ElapsedMilliseconds} ms",
                operation, update.Id, update.ImageDigest, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}