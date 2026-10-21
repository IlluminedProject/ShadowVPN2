using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.Cluster;

public sealed class ClusterUpdateCoordinator(
    ClusterUpdateService updateService,
    IDocumentStore documentStore,
    IOptions<LocalConfiguration> localConfiguration,
    IOptions<UpdaterOptions> options,
    ILogger<ClusterUpdateCoordinator> logger) : BackgroundService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // Reconcile retained socket state after app replacement and retry transient RavenDB/socket outages.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try {
            do {
                try {
                    await ReconcileAsync(stoppingToken);
                    await updateService.ArchiveCompletedAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    break;
                }
                catch (Exception exception) {
                    logger.LogWarning(exception,
                        "Cluster update reconciliation failed for node {NodeTag} using socket {SocketPath}",
                        ClusterService.GetNodeTag(localConfiguration.Value.NodeNumber), options.Value.SocketPath);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken) {
        var localTag = ClusterService.GetNodeTag(localConfiguration.Value.NodeNumber);
        var update = await updateService.GetAsync(cancellationToken);
        if (update is null) return;
        if (update.Status == "Preparing") {
            var localNode = update.Nodes.FirstOrDefault(node => node.NodeTag == localTag);
            if (localNode?.Status == "Preparing") {
                var prepared = await SendAsync("prepare", update, cancellationToken);
                if (Matches(update, prepared) && prepared.Status is "Prepared" or "Succeeded" or "Failed") {
                    await updateService.PrepareLocalAsync(localTag, update.Id,
                        prepared.Status is "Prepared" or "Succeeded", prepared.Error, cancellationToken);
                }
            }

            return;
        }

        if (update.Status != "Running" || update.CurrentNodeTag != localTag)
            return;

        var status = await SendAsync("status", update, cancellationToken);
        var ownsStep = update.Nodes.Any(node => node.NodeTag == localTag && node.Status == "Claimed");
        if (ownsStep && await CompleteAsync(update, localTag, status, cancellationToken))
            return;
        if (Matches(update, status) && status.Status == "Running")
            return;

        var topology = await documentStore.Maintenance.Server.SendAsync(new GetClusterTopologyOperation(),
            cancellationToken);
        var claimed = await updateService.ClaimLocalAsync(localTag, topology, cancellationToken);
        if (claimed is null)
            return;

        // The compare-exchange claim may observe a newer rollout than the initial read.
        if (claimed.Id != update.Id || claimed.ImageDigest != update.ImageDigest)
            return;
        if (await CompleteAsync(claimed, localTag, status, cancellationToken))
            return;
        if (Matches(claimed, status) && status.Status != "Prepared")
            return;
        if (status.Status == "Running") {
            logger.LogWarning(
                "Updater is running operation {UpdaterUpdateId} for digest {UpdaterImageDigest} while node {NodeTag} owns update {UpdateId}",
                status.UpdateId, status.ImageDigest, localTag, claimed.Id);
            return;
        }

        var started = await SendAsync("start", claimed, cancellationToken);
        if (await CompleteAsync(claimed, localTag, started, cancellationToken))
            return;
        if (Matches(claimed, started) && started.Status == "Running") {
            logger.LogInformation("Updater accepted update {UpdateId} for node {NodeTag} with digest {ImageDigest}",
                claimed.Id, localTag, claimed.ImageDigest);
        }
        else {
            logger.LogWarning(
                "Updater did not accept update {UpdateId} for node {NodeTag}; returned operation {UpdaterUpdateId} with status {UpdaterStatus}",
                claimed.Id, localTag, started.UpdateId, started.Status);
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