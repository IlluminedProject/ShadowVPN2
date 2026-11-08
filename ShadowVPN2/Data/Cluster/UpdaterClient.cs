using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Updater;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.Cluster;

public sealed class UpdaterClient(IOptions<UpdaterOptions> options, ILogger<UpdaterClient> logger) {
    private const int DefaultTimeoutSeconds = 15;
    private const int PrepareTimeoutMinutes = 15;

    public async Task<ContainerImageInfo> GetImageInfoAsync(CancellationToken cancellationToken) {
        var request = new UpdaterRequest { Operation = UpdaterOperation.Info };
        var response = await SendAsync(request, cancellationToken);
        return response is { Status: UpdaterStatus.Succeeded, ImageInfo: { } image }
            ? image
            : throw new IOException($"The updater could not inspect the running application image: {response.Error}");
    }

    public async Task<UpdaterResponse> GetStatusAsync(Guid updateId, string imageDigest, CancellationToken cancellationToken) {
        var request = new UpdaterRequest {
            Operation = UpdaterOperation.Status, UpdateId = updateId, ImageDigest = imageDigest
        };
        return await SendAsync(request, cancellationToken);
    }

    public async Task<UpdaterResponse> GetLocalStatusAsync(Guid updateId, string imageDigest, CancellationToken cancellationToken) {
        var request = new UpdaterRequest {
            Operation = UpdaterOperation.LocalStatus, UpdateId = updateId, ImageDigest = imageDigest
        };
        return await SendAsync(request, cancellationToken);
    }

    public async Task<UpdaterResponse> PrepareAsync(Guid updateId, string imageDigest, CancellationToken cancellationToken) {
        var request = new UpdaterRequest {
            Operation = UpdaterOperation.Prepare, UpdateId = updateId, ImageDigest = imageDigest
        };
        return await SendAsync(request, cancellationToken, TimeSpan.FromMinutes(PrepareTimeoutMinutes));
    }

    public async Task<UpdaterResponse> StartAsync(Guid updateId, string imageDigest, CancellationToken cancellationToken) {
        var request = new UpdaterRequest {
            Operation = UpdaterOperation.Start, UpdateId = updateId, ImageDigest = imageDigest
        };
        return await SendAsync(request, cancellationToken);
    }

    private async Task<UpdaterResponse> SendAsync(UpdaterRequest request, CancellationToken cancellationToken, TimeSpan? timeout = null) {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(DefaultTimeoutSeconds);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(effectiveTimeout);

        logger.LogDebug("Sending updater request: operation {Operation}, updateId {UpdateId}, digest {ImageDigest}, timeout {Timeout}",
            request.Operation, request.UpdateId, request.ImageDigest, effectiveTimeout);

        var stopwatch = Stopwatch.StartNew();
        try {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(options.Value.SocketPath), timeoutSource.Token);
            await using var stream = new NetworkStream(socket, false);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            var requestJson = JsonSerializer.Serialize(request, UpdaterJsonContext.Default.UpdaterRequest);
            await writer.WriteLineAsync(requestJson.AsMemory(), timeoutSource.Token);
            await writer.FlushAsync(timeoutSource.Token);

            var responseLine = await reader.ReadLineAsync(timeoutSource.Token) ??
                               throw new IOException("The updater closed the connection without a response");
            var response = JsonSerializer.Deserialize(responseLine, UpdaterJsonContext.Default.UpdaterResponse) ??
                           throw new IOException("The updater returned an empty JSON response");

            logger.LogDebug("Updater operation completed: operation {Operation}, updateId {UpdateId}, status {Status}, elapsed {ElapsedMilliseconds} ms",
                request.Operation, request.UpdateId, response.Status, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception exception) {
            logger.LogError(exception,
                "Updater operation failed: operation {Operation}, updateId {UpdateId}, digest {ImageDigest}, elapsed {ElapsedMilliseconds} ms",
                request.Operation, request.UpdateId, request.ImageDigest, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
