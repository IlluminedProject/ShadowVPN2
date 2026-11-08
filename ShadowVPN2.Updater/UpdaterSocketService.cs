using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Updater;

namespace ShadowVPN2.Updater;

public sealed class UpdaterSocketService(
    ContainerUpdater containerUpdater,
    IOptions<UpdaterOptions> options,
    ILogger<UpdaterSocketService> logger) : BackgroundService {
    private const int DefaultSocketTimeoutSeconds = 15;
    private const int PrepareSocketTimeoutMinutes = 15;
    private readonly HashSet<Task> _connections = [];
    private readonly Lock _connectionsGate = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private Task _operation = Task.CompletedTask;
    private UpdaterResponse _state = new();

    private static TimeSpan GetOperationTimeout(UpdaterOperation operation) =>
        operation == UpdaterOperation.Prepare
            ? TimeSpan.FromMinutes(PrepareSocketTimeoutMinutes)
            : TimeSpan.FromSeconds(DefaultSocketTimeoutSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The updater requires Linux Unix domain sockets");
        var socketPath = Path.GetFullPath(options.Value.SocketPath);
        var directory = Path.GetDirectoryName(socketPath)!;
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead |
                                        UnixFileMode.OtherExecute);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var bound = false;
        try {
            await RemoveStaleSocketAsync(socketPath, lifetime.Token);
            bound = true;
            File.SetUnixFileMode(socketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            listener.Listen(16);

            logger.LogInformation("Updater listening on {SocketPath}", socketPath);
            while (!lifetime.IsCancellationRequested) {
                var connection = await listener.AcceptAsync(lifetime.Token);
                var task = HandleConnectionAsync(connection, lifetime.Token);
                lock (_connectionsGate) _connections.Add(task);
                _ = task.ContinueWith(completed => {
                    lock (_connectionsGate) _connections.Remove(completed);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally {
            await lifetime.CancelAsync();
            listener.Dispose();
            Task[] connections;
            lock (_connectionsGate) connections = _connections.ToArray();
            await Task.WhenAll(connections);
            await _operation;
            if (bound) File.Delete(socketPath);
        }
    }

    private async Task HandleConnectionAsync(Socket connection, CancellationToken stoppingToken) {
        using (connection)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)) {
            try {
                await using var stream = new NetworkStream(connection, false);
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null) return;
                UpdaterRequest request;
                try {
                    request = JsonSerializer.Deserialize(line, UpdaterJsonContext.Default.UpdaterRequest) ??
                              throw new JsonException("An updater request is required");
                    timeout.CancelAfter(GetOperationTimeout(request.Operation));
                }
                catch (JsonException exception) {
                    await WriteAsync(writer,
                        new UpdaterResponse { Status = UpdaterStatus.Failed, Error = exception.Message },
                        timeout.Token);
                    return;
                }

                var stopwatch = Stopwatch.StartNew();
                UpdaterResponse response;
                try {
                    response = request.Operation switch {
                        UpdaterOperation.Status => Volatile.Read(ref _state),
                        UpdaterOperation.LocalStatus => await GetLocalStatusAsync(request, timeout.Token),
                        UpdaterOperation.Prepare => await PrepareAsync(request, timeout.Token),
                        UpdaterOperation.Start => await StartAsync(request, stoppingToken),
                        UpdaterOperation.Info => await GetImageInfoAsync(timeout.Token),
                        _ => new UpdaterResponse {
                            UpdateId = request.UpdateId, ImageDigest = request.ImageDigest,
                            Status = UpdaterStatus.Failed, Error = "Unknown updater operation"
                        }
                    };
                }
                catch (JsonException exception) {
                    response = new UpdaterResponse { Status = UpdaterStatus.Failed, Error = exception.Message };
                }

                logger.LogInformation(
                    "Updater request completed: operation {Operation}, update {UpdateId}, status {Status}, error {Error}, elapsed {ElapsedMilliseconds} ms",
                    request.Operation, request.UpdateId, response.Status, response.Error,
                    stopwatch.ElapsedMilliseconds);
                await WriteAsync(writer, response, timeout.Token);
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested &&
                                                               !stoppingToken.IsCancellationRequested) {
                logger.LogWarning(exception, "Updater socket request timed out");
            }
            catch (Exception exception) { logger.LogWarning(exception, "Updater socket request failed"); }
        }
    }

    private static async Task WriteAsync(StreamWriter writer, UpdaterResponse response, CancellationToken token) {
        await writer.WriteLineAsync(
            JsonSerializer.Serialize(response, UpdaterJsonContext.Default.UpdaterResponse).AsMemory(), token);
        await writer.FlushAsync(token);
    }

    private async Task<UpdaterResponse> GetImageInfoAsync(CancellationToken token) {
        try {
            return new UpdaterResponse
                { Status = UpdaterStatus.Succeeded, ImageInfo = await containerUpdater.GetImageInfoAsync(token) };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            return new UpdaterResponse {
                Status = UpdaterStatus.Failed, Error = exception.Message
            };
        }
    }

    private async Task<UpdaterResponse> GetLocalStatusAsync(UpdaterRequest request, CancellationToken token) {
        try {
            var image = await containerUpdater.GetImageInfoAsync(token);
            var digest = image.RepoDigests.Select(value => value.Split('@', 2)).Where(parts => parts.Length == 2)
                .Select(parts => parts[1]).FirstOrDefault(value => value == request.ImageDigest) ?? "";
            return new UpdaterResponse {
                UpdateId = request.UpdateId, ImageDigest = digest, Status = UpdaterStatus.Succeeded, ImageInfo = image
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            return new UpdaterResponse
                { UpdateId = request.UpdateId, Status = UpdaterStatus.Failed, Error = exception.Message };
        }
    }

    private async Task<UpdaterResponse> PrepareAsync(UpdaterRequest request, CancellationToken token) {
        if (request.UpdateId == Guid.Empty || !ImageDigest.IsValid(request.ImageDigest)) return Invalid(request);
        try {
            await containerUpdater.PrepareAsync(request.ImageDigest, token);
            return new UpdaterResponse {
                UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = UpdaterStatus.Prepared
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            return new UpdaterResponse {
                UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = UpdaterStatus.Failed,
                Error = exception.Message
            };
        }
    }

    private async Task<UpdaterResponse> StartAsync(UpdaterRequest request, CancellationToken stoppingToken) {
        if (request.UpdateId == Guid.Empty || !ImageDigest.IsValid(request.ImageDigest)) return Invalid(request);
        await _startGate.WaitAsync(stoppingToken);
        try {
            var current = Volatile.Read(ref _state);
            if (current.Status == UpdaterStatus.Running) {
                return current with {
                    UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = UpdaterStatus.Failed,
                    Error = "Another update is running"
                };
            }

            if (!await containerUpdater.IsPreparedAsync(request.ImageDigest, stoppingToken))
                return new UpdaterResponse {
                    UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = UpdaterStatus.Failed,
                    Error = "The requested image has not been prepared"
                };

            var running = new UpdaterResponse {
                UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = UpdaterStatus.Running
            };

            Volatile.Write(ref _state, running);
            _operation = Task.Run(() => RunOperationAsync(running, stoppingToken), CancellationToken.None);
            return running;
        }
        finally { _startGate.Release(); }
    }

    private static UpdaterResponse Invalid(UpdaterRequest request) => new() {
        UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = UpdaterStatus.Failed,
        Error = "A nonempty update ID and canonical sha256 image digest are required"
    };

    private async Task RunOperationAsync(UpdaterResponse running, CancellationToken token) {
        try {
            await containerUpdater.UpdateAsync(running.ImageDigest, token);
        }
        catch (Exception) {
            // ignored
        }
    }

    private static async Task RemoveStaleSocketAsync(string socketPath, CancellationToken token) {
        if (!Path.Exists(socketPath)) return;
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try {
            await probe.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
            throw new InvalidOperationException("Another updater is already listening");
        }
        catch (SocketException) { File.Delete(socketPath); }
    }
}