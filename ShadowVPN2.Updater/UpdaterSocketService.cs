using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Updater;

public sealed class UpdaterSocketService(
    ContainerUpdater containerUpdater,
    IOptions<UpdaterOptions> options,
    IHostApplicationLifetime applicationLifetime,
    ILogger<UpdaterSocketService> logger) : BackgroundService {
    private readonly HashSet<Task> _connections = [];
    private readonly object _connectionsGate = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private Task _operation = Task.CompletedTask;
    private UpdaterSocketResponse _state = new();
    private string _statePath = "";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The updater requires Linux Unix domain sockets");

        var socketPath = Path.GetFullPath(options.Value.SocketPath);
        var directory = Path.GetDirectoryName(socketPath)!;
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead |
                                        UnixFileMode.OtherExecute);
        _statePath = Path.Combine(directory, "update.json");
        // Keep the lock file after shutdown: removing it could let two processes lock different inodes.
        using var ownership = new FileStream(Path.Combine(directory, "updater.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var bound = false;
        try {
            await LoadStateAsync(lifetime.Token);
            await RemoveStaleSocketAsync(socketPath, lifetime.Token);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            bound = true;
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                             UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead |
                                             UnixFileMode.OtherWrite);
            listener.Listen(16);
            if (_state.Status == "Running") {
                var resumed = _state;
                _operation = Task.Run(() => RunOperationAsync(resumed, lifetime.Token), CancellationToken.None);
            }

            logger.LogInformation("Updater listening on {SocketPath}", socketPath);
            while (!lifetime.IsCancellationRequested) {
                var connection = await listener.AcceptAsync(lifetime.Token);
                var task = HandleConnectionAsync(connection, lifetime.Token);
                lock (_connectionsGate)
                    _connections.Add(task);
                _ = task.ContinueWith(completed => {
                    lock (_connectionsGate)
                        _connections.Remove(completed);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) {
        }
        finally {
            await lifetime.CancelAsync();
            listener.Dispose();
            Task[] connections;
            lock (_connectionsGate)
                connections = _connections.ToArray();
            await Task.WhenAll(connections);
            await _operation;
            if (bound)
                File.Delete(socketPath);
        }
    }

    private async Task HandleConnectionAsync(Socket connection, CancellationToken stoppingToken) {
        using (connection)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)) {
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try {
                await using var stream = new NetworkStream(connection, false);
                using var reader = new StreamReader(stream, Encoding.UTF8, false,
                    1024, true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true);
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null)
                    return;

                UpdaterSocketResponse response;
                try {
                    var request = JsonSerializer.Deserialize(line, UpdaterJsonContext.Default.UpdaterSocketRequest)
                                  ?? throw new JsonException("An updater request is required");
                    response = request.Operation switch {
                        "status" => Volatile.Read(ref _state),
                        "prepare" => await PrepareAsync(request, timeout.Token),
                        "start" => await StartAsync(request, stoppingToken),
                        "info" => await GetImageInfoAsync(timeout.Token),
                        _ => new UpdaterSocketResponse {
                            UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = "Failed",
                            Error = "Operation must be prepare, start, status or info"
                        }
                    };
                }
                catch (JsonException exception) {
                    logger.LogWarning(exception, "Updater socket request could not be read");
                    response = new UpdaterSocketResponse { Status = "Failed", Error = exception.Message };
                }

                await writer.WriteLineAsync(
                    JsonSerializer.Serialize(response, UpdaterJsonContext.Default.UpdaterSocketResponse).AsMemory(),
                    timeout.Token);
                await writer.FlushAsync(timeout.Token);
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested) {
                if (!stoppingToken.IsCancellationRequested)
                    logger.LogWarning(exception, "Updater socket request timed out");
            }
            catch (Exception exception) {
                logger.LogWarning(exception, "Updater socket request failed");
            }
        }
    }

    private async Task<UpdaterSocketResponse> GetImageInfoAsync(CancellationToken cancellationToken) {
        try {
            return new UpdaterSocketResponse {
                Status = "Succeeded", ImageInfo = await containerUpdater.GetImageInfoAsync(cancellationToken)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            logger.LogWarning(exception, "Could not inspect the running application image");
            return new UpdaterSocketResponse {
                Status = "Failed", Error = "The updater could not inspect the running application image"
            };
        }
    }

    private async Task<UpdaterSocketResponse> PrepareAsync(UpdaterSocketRequest request,
        CancellationToken cancellationToken) {
        if (request.UpdateId == Guid.Empty || !ImageDigest.IsValid(request.ImageDigest)) {
            return new UpdaterSocketResponse {
                UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = "Failed",
                Error = "A nonempty update ID and canonical sha256 image digest are required"
            };
        }

        await _startGate.WaitAsync(cancellationToken);
        try {
            var current = Volatile.Read(ref _state);
            if (current.UpdateId == request.UpdateId && current.ImageDigest == request.ImageDigest &&
                current.Status == "Prepared")
                return current;
            if (current.Status == "Running") {
                return current with {
                    UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = "Failed",
                    Error = "Another update is running"
                };
            }

            await containerUpdater.PrepareAsync(request.ImageDigest, cancellationToken);
            var prepared = new UpdaterSocketResponse
                { UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = "Prepared" };
            await PersistStateAsync(prepared, cancellationToken);
            Volatile.Write(ref _state, prepared);
            return prepared;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            logger.LogWarning(exception, "Could not prepare image {ImageDigest}", request.ImageDigest);
            return new UpdaterSocketResponse {
                UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = "Failed",
                Error = exception.Message
            };
        }
        finally { _startGate.Release(); }
    }

    private async Task<UpdaterSocketResponse> StartAsync(UpdaterSocketRequest request,
        CancellationToken stoppingToken) {
        if (request.UpdateId == Guid.Empty || !ImageDigest.IsValid(request.ImageDigest)) {
            return new UpdaterSocketResponse {
                UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = "Failed",
                Error = "A nonempty update ID and canonical sha256 image digest are required"
            };
        }

        await _startGate.WaitAsync(stoppingToken);
        try {
            var current = Volatile.Read(ref _state);
            if (current.UpdateId != request.UpdateId || current.ImageDigest != request.ImageDigest ||
                current.Status != "Prepared") {
                return new UpdaterSocketResponse {
                    UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = "Failed",
                    Error = "The requested image was not prepared for this update"
                };
            }

            var running = new UpdaterSocketResponse {
                UpdateId = request.UpdateId, ImageDigest = request.ImageDigest, Status = "Running"
            };
            try {
                // Acceptance is durable even if the client disconnects before receiving its response.
                await PersistStateAsync(running, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) {
                logger.LogError(exception, "Could not persist updater operation {UpdateId}", request.UpdateId);
                return running with { Status = "Failed", Error = exception.Message };
            }

            Volatile.Write(ref _state, running);
            _operation = Task.Run(() => RunOperationAsync(running, stoppingToken), CancellationToken.None);
            return running;
        }
        finally {
            _startGate.Release();
        }
    }

    private async Task RunOperationAsync(UpdaterSocketResponse running, CancellationToken stoppingToken) {
        UpdaterSocketResponse completed;
        try {
            await containerUpdater.UpdateAsync(running.ImageDigest, stoppingToken);
            completed = running with { Status = "Succeeded" };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // Leave Running durable so a restart can safely resume the Docker operation.
            return;
        }
        catch (Exception exception) {
            logger.LogError(exception, "Container update failed for {Digest}", running.ImageDigest);
            completed = running with { Status = "Failed", Error = exception.Message };
        }

        await _startGate.WaitAsync(CancellationToken.None);
        try {
            // A finished update must stay finished even when shutdown has already been requested.
            await PersistStateAsync(completed, CancellationToken.None);
            Volatile.Write(ref _state, completed);
        }
        catch (Exception exception) {
            logger.LogCritical(exception, "Could not persist updater result; stopping to resume the durable operation");
            applicationLifetime.StopApplication();
        }
        finally {
            _startGate.Release();
        }
    }

    private async Task LoadStateAsync(CancellationToken cancellationToken) {
        if (!File.Exists(_statePath))
            return;
        await using var stream = File.OpenRead(_statePath);
        var state = await JsonSerializer.DeserializeAsync(stream, UpdaterJsonContext.Default.UpdaterSocketResponse,
                        cancellationToken)
                    ?? throw new InvalidDataException("Updater state is empty");
        if (state.UpdateId == Guid.Empty || !ImageDigest.IsValid(state.ImageDigest) ||
            state.Status is not ("Prepared" or "Running" or "Succeeded" or "Failed"))
            throw new InvalidDataException("Updater state contains an invalid operation");
        Volatile.Write(ref _state, state);
    }

    private async Task PersistStateAsync(UpdaterSocketResponse state, CancellationToken cancellationToken) {
        var temporaryPath = _statePath + ".tmp";
        try {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough)) {
                await JsonSerializer.SerializeAsync(stream, state, UpdaterJsonContext.Default.UpdaterSocketResponse,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }

            File.Move(temporaryPath, _statePath, true);
        }
        finally {
            try { File.Delete(temporaryPath); }
            catch (Exception exception) {
                logger.LogWarning(exception, "Could not remove temporary updater state");
            }
        }
    }

    private static async Task RemoveStaleSocketAsync(string socketPath, CancellationToken cancellationToken) {
        if (!Path.Exists(socketPath))
            return;
        if ((File.GetAttributes(socketPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("The updater socket path is occupied by a directory or symbolic link");

        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try {
            await probe.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused) {
            File.Delete(socketPath);
            return;
        }

        throw new IOException("Another process is listening on the updater socket");
    }
}