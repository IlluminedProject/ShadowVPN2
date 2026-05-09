using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure;
using ShadowVPN2.Infrastructure.Authentication;
using TruePath;
using TruePath.SystemIo;

namespace ShadowVPN2.Data;

public class SingBoxService : BackgroundService
{
    private static readonly AbsolutePath ConfigDir = DataUtils.DataFolder / "sing-box";
    private static readonly AbsolutePath ConfigPath = ConfigDir / "config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly IEnumerable<ISingBoxConfigContributor> _contributors;
    private readonly IDocumentStore _documentStore;
    private readonly GlobalConfigurationService _globalConfigurationService;
    private readonly ILogger<SingBoxService> _logger;
    private readonly ProtocolSettingsService _protocolSettingsService;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isManualRestart;
    private Process? _process;

    public SingBoxService(
        ILogger<SingBoxService> logger,
        IConfiguration configuration,
        IDocumentStore documentStore,
        IEnumerable<ISingBoxConfigContributor> contributors,
        ProtocolSettingsService protocolSettingsService,
        GlobalConfigurationService globalConfigurationService)
    {
        _logger = logger;
        _configuration = configuration;
        _documentStore = documentStore;
        _contributors = contributors;
        _protocolSettingsService = protocolSettingsService;
        _globalConfigurationService = globalConfigurationService;
    }

    private string BinaryPath => _configuration["SingBox:BinaryPath"] ?? "sing-box";

    public bool IsRunning { get; private set; }

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

        // Initial config generation
        try
        {
            await RegenerateAndApplyConfigAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform initial sing-box configuration");
        }

        // Subscribe to changes
        _globalConfigurationService.ConfigurationChanged += OnConfigurationChanged;

        using var clientsSubscription = _documentStore.Changes()
            .ForDocumentsInCollection<EntityClient>()
            .Subscribe(new ActionObserver<DocumentChange>(change =>
            {
                _logger.LogInformation("Clients collection changed, regenerating sing-box config");
                _ = RegenerateAndApplyConfigAsync(CancellationToken.None);
            }));

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
                try
                {
                    await Task.Delay(3000, stoppingToken);
                }
                catch
                {
                }
            }
        }
    }

    private void OnConfigurationChanged(object? sender, EntityGlobalConfiguration e)
    {
        _logger.LogInformation("Global configuration changed, regenerating sing-box config");
        _ = RegenerateAndApplyConfigAsync(CancellationToken.None);
    }

    public async Task RegenerateAndApplyConfigAsync(CancellationToken ct)
    {
        _logger.LogInformation("Regenerating sing-box configuration");

        var protocols = await _protocolSettingsService.GetConfigurationAsync();

        using var session = _documentStore.OpenAsyncSession();
        var clients = await session.Query<EntityClient>().ToListAsync(ct);

        var config = new SingBoxConfig();
        foreach (var contributor in _contributors) await contributor.ContributeAsync(config, protocols, clients);

        var configJson = JsonSerializer.Serialize(config, SerializerOptions);
        await ApplyConfigAsync(configJson);
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
        _globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        await StopSingBoxAsync();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        _process?.Dispose();
        base.Dispose();
    }
}