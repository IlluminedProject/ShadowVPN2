using System.Diagnostics;
using TruePath;
using TruePath.SystemIo;

namespace ShadowVPN2.Data.SingBox;

public sealed class SingBoxProcessHandle : IAsyncDisposable {
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StartupWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SupervisionInterval = TimeSpan.FromSeconds(3);
    private readonly string _binaryPath;
    private readonly AbsolutePath _candidatePath;
    private readonly AbsolutePath _configDirectory;
    private readonly AbsolutePath _configPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly ILogger _logger;
    private readonly CancellationTokenSource _supervisorCancellation = new();
    private bool _disposed;
    private string? _lastSuccessfulJson;
    private Process? _process;
    private Task _processExitTask = Task.CompletedTask;
    private Task? _supervisorTask;

    internal SingBoxProcessHandle(ILogger logger, string binaryPath, AbsolutePath configDirectory) {
        _logger = logger;
        _binaryPath = binaryPath;
        _configDirectory = configDirectory;
        _configPath = configDirectory / "config.json";
        _candidatePath = configDirectory / "config.candidate.json";
    }

    public bool IsRunning {
        get {
            var process = Volatile.Read(ref _process);
            if (process == null) return false;
            try {
                return !process.HasExited;
            }
            catch (InvalidOperationException) {
                return false;
            }
        }
    }

    public async ValueTask DisposeAsync() {
        Task supervisorTask;
        Exception? stopError = null;
        var ownsDisposal = false;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try {
            if (!_disposed) {
                ownsDisposal = true;
                _disposed = true;
                _supervisorCancellation.Cancel();
                try {
                    await StopCoreAsync().ConfigureAwait(false);
                }
                catch (Exception exception) {
                    stopError = exception;
                }
            }

            supervisorTask = _supervisorTask ?? Task.CompletedTask;
        }
        finally {
            _gate.Release();
        }

        await supervisorTask.ConfigureAwait(false);
        if (ownsDisposal) _supervisorCancellation.Dispose();
        if (stopError != null) throw stopError;
    }

    public async Task ApplyConfigAsync(string configJson, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(configJson);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _supervisorTask ??= SuperviseAsync();

            if (_lastSuccessfulJson == configJson) {
                if (!IsRunning) {
                    await WriteActiveConfigAsync(configJson, ct).ConfigureAwait(false);
                    await StartCoreAsync(ct).ConfigureAwait(false);
                }

                return;
            }

            _configDirectory.CreateDirectory();
            try {
                await _candidatePath.WriteAllTextAsync(configJson, ct).ConfigureAwait(false);
                await CheckConfigCoreAsync(_candidatePath, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                await StopCoreAsync().ConfigureAwait(false);

                try {
                    _candidatePath.Move(_configPath, true);
                    await StartCoreAsync(ct).ConfigureAwait(false);
                }
                catch {
                    await RollBackCoreAsync().ConfigureAwait(false);
                    throw;
                }

                _lastSuccessfulJson = configJson;
            }
            finally {
                try {
                    _candidatePath.Delete();
                }
                catch (Exception exception) {
                    _logger.LogWarning(exception, "Failed to delete sing-box config candidate {ConfigPath}",
                        _candidatePath);
                }
            }
        }
        finally {
            _gate.Release();
        }
    }

    private async Task SuperviseAsync() {
        while (true) {
            try {
                await Task.Delay(SupervisionInterval, _supervisorCancellation.Token).ConfigureAwait(false);
                await _gate.WaitAsync(_supervisorCancellation.Token).ConfigureAwait(false);
                try {
                    if (_disposed) return;
                    if (_lastSuccessfulJson != null && !IsRunning) {
                        await WriteActiveConfigAsync(_lastSuccessfulJson, _supervisorCancellation.Token)
                            .ConfigureAwait(false);
                        await StartCoreAsync(_supervisorCancellation.Token).ConfigureAwait(false);
                    }
                }
                finally {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException) when (_supervisorCancellation.IsCancellationRequested) {
                return;
            }
            catch (Exception exception) {
                _logger.LogError(exception, "Failed to restart sing-box {ConfigPath}; retrying in {RetryDelay}",
                    _configPath, SupervisionInterval);
            }
        }
    }

    private async Task RollBackCoreAsync() {
        try {
            if (_lastSuccessfulJson == null) {
                _configPath.Delete();
                return;
            }

            await WriteActiveConfigAsync(_lastSuccessfulJson, CancellationToken.None).ConfigureAwait(false);
            await StartCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) {
            _logger.LogError(exception, "Failed to restore the last successful sing-box configuration {ConfigPath}",
                _configPath);
        }
    }

    private async Task WriteActiveConfigAsync(string configJson, CancellationToken ct) {
        _configDirectory.CreateDirectory();
        await _configPath.WriteAllTextAsync(configJson, ct).ConfigureAwait(false);
    }

    private async Task CheckConfigCoreAsync(AbsolutePath configPath, CancellationToken ct) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);
        using var checkProcess = new Process { StartInfo = CreateStartInfo("check", configPath) };
        var started = false;
        try {
            if (!checkProcess.Start())
                throw new InvalidOperationException("Failed to start sing-box config validation");
            started = true;
            var stdout = checkProcess.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = checkProcess.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr, checkProcess.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            if (checkProcess.ExitCode != 0) {
                throw new InvalidOperationException(
                    $"sing-box config validation failed: {await stderr.ConfigureAwait(false)} {await stdout.ConfigureAwait(false)}");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new TimeoutException($"sing-box config validation exceeded {CheckTimeout.TotalSeconds} seconds");
        }
        finally {
            if (started && !checkProcess.HasExited) {
                try {
                    checkProcess.Kill(true);
                }
                catch (InvalidOperationException) when (checkProcess.HasExited) {
                    // Validation exited while cancellation requested termination.
                }

                await checkProcess.WaitForExitAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task StartCoreAsync(CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if (IsRunning) return;
        if (!_configPath.ExistsFile())
            throw new FileNotFoundException("Sing-box configuration not found", _configPath.Value);

        await StopCoreAsync().ConfigureAwait(false);
        var process = new Process {
            StartInfo = CreateStartInfo("run", _configPath),
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, args) => {
            if (!string.IsNullOrEmpty(args.Data))
                _logger.LogInformation("[sing-box:{ConfigPath}] {Data}", _configPath, args.Data);
        };
        process.ErrorDataReceived += (_, args) => {
            if (!string.IsNullOrEmpty(args.Data))
                _logger.LogError("[sing-box:{ConfigPath}] {Data}", _configPath, args.Data);
        };
        process.Exited += (_, _) => {
            if (!ReferenceEquals(Volatile.Read(ref _process), process)) return;
            try {
                if (process.ExitCode != 0) {
                    _logger.LogError("Sing-box {ConfigPath} exited unexpectedly with code {ExitCode}",
                        _configPath, process.ExitCode);
                }
                else
                    _logger.LogInformation("Sing-box {ConfigPath} stopped", _configPath);
            }
            catch (InvalidOperationException) {
                // A concurrent stop may have disposed this process after the identity check.
            }
        };

        try {
            _logger.LogInformation("Starting sing-box {ConfigPath}", _configPath);
            if (!process.Start()) throw new InvalidOperationException("Failed to start sing-box");
            Volatile.Write(ref _process, process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _processExitTask = process.WaitForExitAsync();
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
            startup.CancelAfter(StartupWindow);
            try {
                await _processExitTask.WaitAsync(startup.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                if (!process.HasExited) return;
            }

            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException($"sing-box exited during startup with code {process.ExitCode}");
        }
        catch {
            try {
                if (ReferenceEquals(Volatile.Read(ref _process), process))
                    await StopCoreAsync().ConfigureAwait(false);
                else
                    process.Dispose();
            }
            catch (Exception exception) {
                _logger.LogError(exception, "Failed to clean up sing-box after a startup error {ConfigPath}",
                    _configPath);
            }

            throw;
        }
    }

    private ProcessStartInfo CreateStartInfo(string command, AbsolutePath configPath) {
        var startInfo = new ProcessStartInfo {
            FileName = _binaryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = configPath.Parent!.Value.Value
        };
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configPath.Value);
        return startInfo;
    }

    private async Task StopCoreAsync() {
        var process = Interlocked.Exchange(ref _process, null);
        var exitTask = _processExitTask;
        _processExitTask = Task.CompletedTask;
        if (process == null) return;
        try {
            if (!process.HasExited) {
                _logger.LogInformation("Stopping sing-box process {ProcessId}", process.Id);
                try {
                    process.Kill(true);
                }
                catch (InvalidOperationException) when (process.HasExited) {
                    // The process exited between checking its state and requesting termination.
                }

                await process.WaitForExitAsync().ConfigureAwait(false);
            }

            await exitTask.ConfigureAwait(false);
        }
        finally {
            process.Dispose();
        }
    }
}