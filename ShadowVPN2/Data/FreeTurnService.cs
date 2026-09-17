using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data;

public sealed class FreeTurnService(
    IDocumentStore documentStore,
    GlobalConfigurationService globalConfigurationService,
    FreeTurnClientIdService clientIdService,
    IOptions<FreeTurnOptions> options,
    ILogger<FreeTurnService> logger) : BackgroundService {
    private static readonly JsonSerializerOptions ClientsSerializerOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly string _clientsFile = Path.Combine(DataUtils.DataFolder.Value, "free-turn", "clients.json");
    private readonly Dictionary<Guid, RunningInstance> _instances = new();
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);

    private readonly Channel<bool> _reconcileRequests = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly string _workingDirectory = Path.Combine(DataUtils.DataFolder.Value, "free-turn");

    public bool IsHealthy { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("FreeTurnService starting");
        Directory.CreateDirectory(_workingDirectory);

        globalConfigurationService.ConfigurationChanged += OnConfigurationChanged;
        using var clientsSubscription = documentStore.Changes()
            .ForDocumentsInCollection<EntityClient>()
            .Subscribe(new ActionObserver<DocumentChange>(_ => RequestReconcile()));

        await ReconcileAsync(stoppingToken);

        try {
            while (await _reconcileRequests.Reader.WaitToReadAsync(stoppingToken)) {
                while (_reconcileRequests.Reader.TryRead(out _)) {
                }

                await ReconcileAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        }
        finally {
            globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
            await StopAllAsync();
            IsHealthy = true;
        }
    }

    private void OnConfigurationChanged(object? sender, EntityGlobalConfiguration configuration) {
        RequestReconcile();
    }

    private void RequestReconcile() {
        _reconcileRequests.Writer.TryWrite(true);
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken) {
        await _reconcileLock.WaitAsync(cancellationToken);
        try {
            var configuration = await globalConfigurationService.GetAsync(cancellationToken);
            var clients = await GetClientsAsync(cancellationToken);

            try {
                GlobalConfigurationValidator.Validate(configuration);
            }
            catch (ArgumentException exception) {
                logger.LogError(exception, "Invalid global configuration for FreeTurn");
                await StopAllAsync();
                IsHealthy = false;
                return;
            }

            await WriteClientsFileAsync(clients, cancellationToken);

            var protocols = configuration.Protocols.ToDictionary(protocol => protocol.Id);
            var desired = configuration.Transports
                .OfType<FreeTurnTransportSettings>()
                .Where(transport => transport.Enabled)
                .Select(transport => {
                    protocols.TryGetValue(transport.ProtocolId, out var protocol);
                    return (transport, protocol);
                })
                .Where(pair => pair.protocol is { Enabled: true })
                .ToDictionary(pair => pair.transport.Id,
                    pair => BuildArguments(pair.transport, pair.protocol!));

            foreach (var instance in _instances.Keys.Except(desired.Keys).ToList())
                await StopAsync(instance);

            foreach (var (transportId, arguments) in desired) {
                if (_instances.TryGetValue(transportId, out var running) &&
                    running.Signature == GetSignature(arguments) &&
                    !running.Process.HasExited)
                    continue;

                if (_instances.ContainsKey(transportId))
                    await StopAsync(transportId);

                Start(transportId, arguments);
            }

            IsHealthy = desired.Keys.All(transportId =>
                _instances.TryGetValue(transportId, out var instance) && !instance.Process.HasExited);
        }
        finally {
            _reconcileLock.Release();
        }
    }

    private async Task<IReadOnlyList<EntityClient>> GetClientsAsync(CancellationToken cancellationToken) {
        using var session = documentStore.OpenAsyncSession();
        return await session.Query<EntityClient>().ToListAsync(cancellationToken);
    }

    private async Task WriteClientsFileAsync(IReadOnlyList<EntityClient> clients,
        CancellationToken cancellationToken) {
        var data = new FreeTurnClientsFile {
            Clients = clients
                .Where(client => client.IsEnabled)
                .ToDictionary(
                    client => clientIdService.GetClientId(client.SubscriptionId),
                    client => new FreeTurnClientInfo { Comment = client.Name })
        };

        var temporaryPath = _clientsFile + ".tmp";
        var json = JsonSerializer.Serialize(data, ClientsSerializerOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _clientsFile, true);
    }

    private string[] BuildArguments(FreeTurnTransportSettings transport, ProtocolGlobalSettings protocol) {
        var arguments = new List<string> {
            "-listen", $"0.0.0.0:{transport.ListenPort}",
            "-connect", $"127.0.0.1:{protocol.ListenPort}",
            "-mode", ProtocolDefinitionMetadata.GetSocketKind(protocol).ToFreeTurnMode(),
            "-clients-file", _clientsFile
        };

        if (transport.ObfuscationProfile is { } profile) {
            arguments.Add("-obf-profile");
            arguments.Add(profile.ToCommandLineValue());
            arguments.Add("-obf-key");
            arguments.Add(transport.ObfuscationKey!);
        }

        return arguments.ToArray();
    }

    private void Start(Guid transportId, string[] arguments) {
        if (!File.Exists(options.Value.BinaryPath)) {
            logger.LogWarning("FreeTurn server binary not found at {BinaryPath}", options.Value.BinaryPath);
            return;
        }

        try {
            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = options.Value.BinaryPath,
                    WorkingDirectory = _workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.OutputDataReceived += (_, eventArgs) => LogOutput(eventArgs.Data, false);
            process.ErrorDataReceived += (_, eventArgs) => LogOutput(eventArgs.Data, true);
            process.Exited += (_, _) => {
                logger.LogWarning("FreeTurn server process stopped for transport {TransportId} with code {ExitCode}",
                    transportId, process.ExitCode);
                RequestReconcile();
            };

            if (!process.Start()) {
                process.Dispose();
                logger.LogError("Failed to start FreeTurn server for transport {TransportId}", transportId);
                return;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _instances[transportId] = new RunningInstance(process, GetSignature(arguments));
            logger.LogInformation("FreeTurn server started for transport {TransportId}", transportId);
        }
        catch (Exception exception) {
            logger.LogError(exception, "Failed to start FreeTurn server for transport {TransportId}", transportId);
        }
    }

    private void LogOutput(string? data, bool error) {
        if (string.IsNullOrWhiteSpace(data))
            return;

        if (error)
            logger.LogError("[FreeTurn] {Data}", data);
        else
            logger.LogInformation("[FreeTurn] {Data}", data);
    }

    private async Task StopAsync(Guid transportId) {
        if (!_instances.Remove(transportId, out var instance))
            return;

        try {
            if (!instance.Process.HasExited)
                instance.Process.Kill(true);

            await instance.Process.WaitForExitAsync();
        }
        catch (InvalidOperationException) {
        }
        finally {
            instance.Process.Dispose();
        }
    }

    private async Task StopAllAsync() {
        foreach (var transportId in _instances.Keys.ToList())
            await StopAsync(transportId);
    }

    private static string GetSignature(IEnumerable<string> arguments) {
        return string.Join("\0", arguments);
    }

    public override void Dispose() {
        foreach (var instance in _instances.Values) {
            try {
                if (!instance.Process.HasExited)
                    instance.Process.Kill(true);
            }
            catch (InvalidOperationException) {
            }

            instance.Process.Dispose();
        }

        _instances.Clear();
        _reconcileRequests.Writer.TryComplete();
        _reconcileLock.Dispose();
        base.Dispose();
    }

    private sealed record RunningInstance(Process Process, string Signature);

    private sealed class FreeTurnClientsFile {
        [JsonPropertyName("clients")] public Dictionary<string, FreeTurnClientInfo> Clients { get; init; } = new();
    }

    private sealed class FreeTurnClientInfo {
        [JsonPropertyName("comment")] public string? Comment { get; init; }
    }
}