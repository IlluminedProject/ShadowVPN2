using System.Diagnostics;
using ShadowVPN2.Infrastructure;
using TruePath;
using TruePath.SystemIo;

namespace ShadowVPN2.Data;

public class SingBoxService : BackgroundService
{
    private readonly ILogger<SingBoxService> _logger;
    private readonly IConfiguration _configuration;
    private Process? _process;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isManualRestart = false;

    private string BinaryPath => _configuration["SingBox:BinaryPath"] ?? "sing-box";
    private static readonly AbsolutePath ConfigDir = DataUtils.DataFolder / "sing-box";
    private static readonly AbsolutePath ConfigPath = ConfigDir / "config.json";

    public bool IsRunning { get; private set; }

    public SingBoxService(ILogger<SingBoxService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SingBoxService starting");

        var binaryPath = BinaryPath;
        if (binaryPath != "sing-box" && !File.Exists(binaryPath))
        {
            _logger.LogCritical("sing-box binary not found at {Path}", binaryPath);
            throw new FileNotFoundException($"sing-box binary not found at {binaryPath}");
        }

        if (!ConfigDir.ExistsDirectory())
        {
            ConfigDir.CreateDirectory();
        }

        if (!ConfigPath.ExistsFile())
        {
            _logger.LogInformation("sing-box config not found, generating minimal default config");
            const string minimalConfig = "{\"log\":{\"level\":\"info\"},\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"}]}";
            await ConfigPath.WriteAllTextAsync(minimalConfig, cancellationToken: stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            Process? currentProcess = null;

            await _semaphore.WaitAsync(stoppingToken);
            try
            {
                if (_process == null || _process.HasExited)
                {
                    StartProcessInternal(binaryPath);
                }
                currentProcess = _process;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start sing-box process");
            }
            finally
            {
                _semaphore.Release();
            }

            if (currentProcess != null)
            {
                try
                {
                    await currentProcess.WaitForExitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (_isManualRestart)
            {
                _isManualRestart = false;
            }
            else
            {
                _logger.LogWarning("sing-box process exited unexpectedly. Restarting in 3 seconds...");
                try { await Task.Delay(3000, stoppingToken); } catch { }
            }
        }
    }

    public async Task ApplyConfigAsync(string configJson)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!ConfigDir.ExistsDirectory())
            {
                ConfigDir.CreateDirectory();
            }

            var tempConfigPath = ConfigDir / "config_temp.json";
            await tempConfigPath.WriteAllTextAsync(configJson);

            // Validate config
            var binaryPath = BinaryPath;
            var checkProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = binaryPath,
                    Arguments = $"check -c {tempConfigPath.Value}",
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

            if (checkProcess.ExitCode != 0)
            {
                _logger.LogError("sing-box config validation failed: {Error} {Output}", stderr, stdout);
                if (tempConfigPath.ExistsFile())
                {
                    File.Delete(tempConfigPath.Value);
                }
                throw new InvalidOperationException($"sing-box config validation failed: {stderr} {stdout}");
            }

            // Valid, replace actual config
            File.Move(tempConfigPath.Value, ConfigPath.Value, overwrite: true);

            _logger.LogInformation("Config applied successfully. Restarting sing-box");

            _isManualRestart = true;
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                // We do not WaitForExitAsync here because ExecuteAsync is waiting for it
                // and will restart the process immediately since _isManualRestart is true.
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void StartProcessInternal(string binaryPath)
    {
        _logger.LogInformation("Starting sing-box");

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = $"run -c {ConfigPath.Value}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = ConfigDir.Value
            }
        };

        _process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                _logger.LogInformation("[sing-box] {Data}", args.Data);
        };

        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                _logger.LogError("[sing-box] {Data}", args.Data);
        };

        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            IsRunning = false;
            if (_process != null && _process.ExitCode != 0 && !_isManualRestart)
            {
                _logger.LogError("sing-box process exited unexpectedly with code {ExitCode}", _process.ExitCode);
            }
            else
            {
                _logger.LogInformation("sing-box process exited cleanly");
            }
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        IsRunning = true;
    }

    public async Task StopSingBoxAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            _isManualRestart = true; // Prevent automatic restart
            if (_process != null && !_process.HasExited)
            {
                _logger.LogInformation("Stopping sing-box process");
                _process.Kill();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopSingBoxAsync();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _process?.Dispose();
        base.Dispose();
    }
}
