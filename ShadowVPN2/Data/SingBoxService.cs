using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class SingBoxService(
    ILogger<SingBoxService> logger,
    IDocumentStore documentStore,
    IEnumerable<ISingBoxConfigContributor> contributors,
    GlobalConfigurationService globalConfigurationService,
    ManagedCertificateService managedCertificateService,
    SingBoxProcessManager processManager,
    IEnumerable<ISingBoxExtension> extensions,
    IHostApplicationLifetime applicationLifetime,
    SingBoxRuntimeClient runtimeClient,
    SingBoxRuntimeEndpoint runtimeEndpoint)
    : BackgroundService {
    public static readonly JsonSerializerOptions SerializerOptions = new() {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _configurationGate = new(1, 1);
    private readonly ISingBoxExtension[] _extensions = extensions.ToArray();
    private readonly CancellationTokenSource _lifetime = new();

    private readonly SingBoxProcessHandle _process = processManager.Create("proxy");
    private readonly SingBoxRuntimeClient _runtimeClient = runtimeClient;
    private int _disposed;
    private volatile bool _stopping;

    public bool IsRunning {
        get => _process.IsRunning;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("SingBoxService starting");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = applicationLifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);

        // Subscribe to changes
        globalConfigurationService.ConfigurationChanged += OnConfigurationChanged;
        managedCertificateService.CertificatesChanged += OnCertificatesChanged;

        using var clientsSubscription = documentStore.Changes()
            .ForDocumentsInCollection<EntityClient>()
            .Subscribe(new ActionObserver<DocumentChange>(change => {
                logger.LogInformation("Clients collection changed, regenerating sing-box config");
                _ = RegenerateInBackgroundAsync();
            }));


        await RegenerateInBackgroundAsync();

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private void OnConfigurationChanged(object? sender, EntityGlobalConfiguration e) {
        logger.LogInformation("Global configuration changed, regenerating sing-box config");
        _ = RegenerateInBackgroundAsync();
    }

    private Task OnCertificatesChanged() {
        logger.LogInformation("Managed certificates changed, regenerating sing-box config");
        return RegenerateAndApplyConfigAsync(CancellationToken.None);
    }

    private async Task RegenerateInBackgroundAsync() {
        try {
            await RegenerateAndApplyConfigAsync(CancellationToken.None);
        }
        catch (Exception exception) {
            logger.LogError(exception, "Failed to regenerate and apply sing-box configuration");
        }
    }

    public async Task RegenerateAndApplyConfigAsync(CancellationToken ct) {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        ct = cancellation.Token;
        await _configurationGate.WaitAsync(ct);
        try {
            if (_stopping) return;
            logger.LogInformation("Regenerating sing-box configuration");
            var configuration = await globalConfigurationService.GetAsync(ct);
            var protocols = configuration.Protocols;
            using var session = documentStore.OpenAsyncSession();
            var clients = await session.Query<EntityClient>().ToListAsync(ct);

            var config = new SingBoxConfig();
            config.Log.Level = "debug";
            foreach (var contributor in contributors) await contributor.ContributeAsync(config, protocols, clients);
            var context = new SingBoxContext(config, clients, protocols, configuration.Transports);
            foreach (var extension in _extensions) await extension.ConfigureAsync(context, ct);

            config.Experimental ??= new SingBoxExperimentalConfig();
            config.Experimental.ClashApi ??= new SingBoxClashApiConfig {
                ExternalController = runtimeEndpoint.Address,
                Secret = runtimeEndpoint.Secret
            };
            var runtimeCandidate = _process.GetRuntimeApplyCandidate(config);
            if (runtimeCandidate != null &&
                await TryApplyRuntimeAsync(config, runtimeCandidate, ct).ConfigureAwait(false)) {
                logger.LogInformation("Applied runtime-only configuration change without restart");
                _process.MarkRuntimeApplied(config);
            }
            else {
                await _process.ApplyConfigAsync(config, ct);
            }

            foreach (var extension in _extensions) await extension.ApplyAsync(context, ct);
        }
        finally {
            _configurationGate.Release();
        }
    }

    /// <summary>
    ///     RavenDB-first runtime application: the database is the source of truth,
    ///     and only the deltas (users/peers) are pushed to the running instance.
    ///     Returns false when the runtime path fails; the caller then falls back to
    ///     a full restart.
    /// </summary>
    private async Task<bool>
        TryApplyRuntimeAsync(SingBoxConfig candidate, SingBoxConfig applied, CancellationToken ct) {
        try {
            var appliedInbounds = applied.Inbounds.ToDictionary(i => i.Tag, i => i);
            foreach (var inbound in candidate.Inbounds) {
                if (!appliedInbounds.TryGetValue(inbound.Tag, out var previous) ||
                    InboundsEqual(previous, inbound)) continue;
                if (inbound is not IRuntimeManagedInbound runtimeInbound)
                    return false;
                await _runtimeClient.ReplaceInboundUsersAsync(inbound.Tag, runtimeInbound.RuntimeUsers, ct)
                    .ConfigureAwait(false);
            }

            var appliedEndpoints = applied.Endpoints.ToDictionary(e => e.Tag, e => e);
            foreach (var endpoint in candidate.Endpoints) {
                if (endpoint is not WireGuardEndpointConfig wireGuard ||
                    appliedEndpoints.GetValueOrDefault(endpoint.Tag) is not WireGuardEndpointConfig previousWireGuard)
                    continue;
                if (PeersEqual(previousWireGuard.Peers, wireGuard.Peers)) continue;
                IReadOnlyList<WireGuardPeerDto> peers = [
                    .. wireGuard.Peers
                        .Select(p => new WireGuardPeerDto {
                            PublicKey = p.PublicKey,
                            AllowedIps = p.AllowedIps,
                            PersistentKeepaliveInterval = p.PersistentKeepaliveInterval,
                            Address = p.Address,
                            Port = p.Port
                        })
                ];
                await _runtimeClient.ReplaceEndpointPeersAsync(endpoint.Tag, peers, ct).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception exception) {
            logger.LogWarning(exception, "Runtime-only configuration apply failed; falling back to restart");
            return false;
        }
    }

    private static bool InboundsEqual(InboundConfig left, InboundConfig right) {
        return JsonSerializer.Serialize(left, SerializerOptions) == JsonSerializer.Serialize(right, SerializerOptions);
    }

    private static bool PeersEqual(IReadOnlyList<WireGuardPeer> left, IReadOnlyList<WireGuardPeer> right) {
        return left.Count == right.Count && !left.Zip(right).Any(pair =>
            pair.First.PublicKey != pair.Second.PublicKey ||
            !pair.First.AllowedIps.SequenceEqual(pair.Second.AllowedIps) ||
            pair.First.PersistentKeepaliveInterval != pair.Second.PersistentKeepaliveInterval ||
            pair.First.Address != pair.Second.Address ||
            pair.First.Port != pair.Second.Port);
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        _stopping = true;
        await _lifetime.CancelAsync();
        globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        managedCertificateService.CertificatesChanged -= OnCertificatesChanged;
        await _configurationGate.WaitAsync(CancellationToken.None);
        _configurationGate.Release();
        try {
            await base.StopAsync(cancellationToken);
        }
        finally {
            await _process.DisposeAsync();
        }
    }

    public override void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stopping = true;
        _lifetime.Cancel();
        globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        managedCertificateService.CertificatesChanged -= OnCertificatesChanged;
        try {
            base.Dispose();
        }
        finally {
            _configurationGate.Wait();
            try {
                _process.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally {
                _configurationGate.Release();
            }
        }
    }
}