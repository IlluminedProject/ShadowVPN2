using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly.Registry;
using Raven.Client.Documents;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.Cluster;

public sealed class ClusterUpdateCoordinator(
    ClusterUpdateService updateService,
    IDocumentStore documentStore,
    IOptions<UpdaterOptions> options,
    ResiliencePipelineProvider<string> resiliencePipelines,
    ILogger<ClusterUpdateCoordinator> logger) : BackgroundService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private (Guid UpdateId, string NodeTag, string Error)? _pendingFailure;
    private (Guid UpdateId, string NodeTag)? _activeNode;


    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // Reconcile retained socket state after app replacement and retry transient RavenDB/socket outages.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try {
            do {
                try {
                    await resiliencePipelines.GetPipeline("cluster-update").ExecuteAsync(async token => {
                        try {
                            await ReconcileAsync(token);
                            await updateService.ArchiveCompletedAsync(token);
                        }
                        catch (Exception exception) when (!stoppingToken.IsCancellationRequested) {
                            if (_activeNode is { } active && _pendingFailure is null)
                                _pendingFailure = (active.UpdateId, active.NodeTag, exception.GetBaseException().Message);
                            throw;
                        }
                    }, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    break;
                }
                catch (Exception exception) {
                    logger.LogError(exception,
                        "Cluster update reconciliation retry pipeline exhausted its attempts");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken) {
        if (_pendingFailure is { } failure) {
            await updateService.RecordReconciliationFailureAsync(failure.UpdateId, failure.NodeTag,
                failure.Error, 5, cancellationToken);
            _pendingFailure = null;
        }
        var topology = await documentStore.Maintenance.Server.SendAsync(new GetClusterTopologyOperation(),
            cancellationToken);
        var localTag = topology.NodeTag;
        var update = await updateService.GetAsync(cancellationToken);
        if (update is null) {
            _activeNode = null;
            return;
        }
        _activeNode = update.Status == "Preparing" && update.Nodes.Any(node => node.NodeTag == localTag && node.Status == "Preparing") ||
            update.Status == "Running" && update.CurrentNodeTag == localTag ? (update.Id, localTag) : null;
        if (update.Status == "Preparing") {
            var localNode = update.Nodes.FirstOrDefault(node => node.NodeTag == localTag);
            if (localNode?.Status == "Preparing") {
                var prepared = await SendAsync("prepare", update, cancellationToken);
                if (!Matches(update, prepared) || prepared.Status is not ("Prepared" or "Succeeded" or "Failed"))
                    throw new InvalidOperationException($"Updater did not prepare update {update.Id}: {prepared.Status}; {prepared.Error}");
                await updateService.PrepareLocalAsync(localTag, update.Id,
                    prepared.Status is "Prepared" or "Succeeded", prepared.Error, cancellationToken);
            }

            return;
        }

        if (update.Status != "Running" || update.CurrentNodeTag != localTag)
            return;
        await ReconcileNodeAsync(update, localTag, topology, cancellationToken);
    }

    private async Task ReconcileNodeAsync(ClusterUpdateResponse update, string localTag,
        Raven.Client.Http.ClusterTopologyResponse topology, CancellationToken cancellationToken) {
        var status = await SendAsync("status", update, cancellationToken);
        var ownsStep = update.Nodes.Any(node => node.NodeTag == localTag && node.Status == "Claimed");
        if (ownsStep && await CompleteAsync(update, localTag, status, cancellationToken))
            return;
        if (Matches(update, status) && status.Status == "Running")
            return;

        var claimed = await updateService.ClaimLocalAsync(localTag, topology, cancellationToken);
        if (claimed is null)
            throw new InvalidOperationException("Cannot claim update step: RavenDB restart quorum is unavailable");

        // The compare-exchange claim may observe a newer rollout than the initial read.
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
            throw new InvalidOperationException($"Updater did not accept update {claimed.Id}: {started.Status}; {started.Error}");
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
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
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
        return response is not null
            ? JsonSerializer.Deserialize<UpdaterResponse>(response, JsonOptions) ??
              throw new IOException("Updater returned an empty JSON response")
            : throw new IOException("Updater closed the socket without a response");
    }
}