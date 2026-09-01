using System.Diagnostics;
using Microsoft.Extensions.Options;
using ShadowVPN2.Infrastructure;
using TruePath;
using TruePath.SystemIo;

namespace ShadowVPN2.Data.SingBox;

public class SingBoxProcessManager : IDisposable {
    private readonly string _binaryPath;
    private readonly AbsolutePath _configDir;
    private readonly AbsolutePath _configPath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isManualRestart;
    private Process? _process;

    public SingBoxProcessManager(ILogger<SingBoxProcessManager> logger, IOptions<SingBoxOptions> options) {
        _logger = logger;
        _binaryPath = options.Value.BinaryPath ?? "sing-box";
        _configDir = DataUtils.DataFolder / "sing-box";
        _configPath = _configDir / "config.json";
    }

    public bool IsRunning { get; private set; }

    public void Dispose() {
        _semaphore.Dispose();
        _process?.Kill();
        _process?.Dispose();
    }

    public async Task ApplyConfigAsync(string configJson) {
        await _semaphore.WaitAsync();
        try {
            if (!_configDir.ExistsDirectory()) _configDir.CreateDirectory();

            var tempConfigPath = _configDir / "config_temp.json";
            await tempConfigPath.WriteAllTextAsync(configJson);

            // Validate config
            var checkProcess = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = _binaryPath,
                    Arguments = $"check -c \"{tempConfigPath.Value}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            checkProcess.Start();
            var stderr = await checkProcess.StandardError.ReadToEndAsync();
            var stdout = await checkProcess.StandardOutput.ReadToEndAsync();
            await checkProcess.WaitForExitAsync();

            if (checkProcess.ExitCode != 0) {
                if (tempConfigPath.ExistsFile()) File.Delete(tempConfigPath.Value);

                throw new InvalidOperationException($"sing-box config validation failed: {stderr} {stdout}");
            }

            // Valid, replace actual config
            File.Move(tempConfigPath.Value, _configPath.Value, true);

            _logger.LogInformation("Sing-box configuration updated successfully");

            if (_process != null && !_process.HasExited) {
                _logger.LogInformation("Restarting sing-box to apply changes");
                _isManualRestart = true;
                _process.Kill();
            }
        }
        finally {
            _semaphore.Release();
        }
    }

    public void Start() {
        if (IsRunning && _process != null && !_process.HasExited)
            return;

        _logger.LogInformation("Starting sing-box process");

        if (!_configPath.ExistsFile()) {
            _logger.LogWarning("Cannot start sing-box: config.json not found at {Path}", _configPath);
            return;
        }

        _process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = _binaryPath,
                Arguments = $"run -c \"{_configPath.Value}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _configDir.Value
            }
        };

        _process.OutputDataReceived += (_, args) => {
            if (!string.IsNullOrEmpty(args.Data))
                _logger.LogInformation("[sing-box] {Data}", args.Data);
        };

        _process.ErrorDataReceived += (_, args) => {
            if (!string.IsNullOrEmpty(args.Data))
                _logger.LogError("[sing-box] {Data}", args.Data);
        };

        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => {
            IsRunning = false;
            if (_process != null && _process.ExitCode != 0 && !_isManualRestart)
                _logger.LogError("sing-box process exited unexpectedly with code {ExitCode}", _process.ExitCode);
            else
                _logger.LogInformation("sing-box process stopped");

            _isManualRestart = false;
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        IsRunning = true;
    }

    public void Stop() {
        _semaphore.Wait();
        try {
            _isManualRestart = true;
            if (_process != null && !_process.HasExited) {
                _logger.LogInformation("Stopping sing-box process");
                _process.Kill();
            }
        }
        finally {
            _semaphore.Release();
        }
    }

    public async Task WaitForExitAsync(CancellationToken ct) {
        if (_process != null) await _process.WaitForExitAsync(ct);
    }
}