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
    SingBoxProcessManager singBoxProcessManager,
    IEnumerable<ISingBoxExtension> extensions,
    IHostApplicationLifetime applicationLifetime)
    : BackgroundService {
    public static readonly JsonSerializerOptions SerializerOptions = new() {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim configurationGate = new(1, 1);
    private readonly ISingBoxExtension[] extensions = extensions.ToArray();
    private volatile bool stopping;

    public bool IsRunning {
        get => singBoxProcessManager.IsRunning;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("SingBoxService starting");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = applicationLifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
        await RegenerateAndApplyConfigAsync(stoppingToken);

        // Subscribe to changes
        globalConfigurationService.ConfigurationChanged += OnConfigurationChanged;
        managedCertificateService.CertificatesChanged += OnCertificatesChanged;

        using var clientsSubscription = documentStore.Changes()
            .ForDocumentsInCollection<EntityClient>()
            .Subscribe(new ActionObserver<DocumentChange>(change => {
                logger.LogInformation("Clients collection changed, regenerating sing-box config");
                _ = RegenerateInBackgroundAsync();
            }));

        using var nodesSubscription = documentStore.Changes()
            .ForDocumentsInCollection<EntityClusterNode>()
            .Subscribe(new ActionObserver<DocumentChange>(change => {
                logger.LogInformation("Nodes collection changed, regenerating sing-box config");
                _ = RegenerateInBackgroundAsync();
            }));

        while (!stopping && !stoppingToken.IsCancellationRequested) {
            singBoxProcessManager.Start();
            await singBoxProcessManager.WaitForExitAsync(stoppingToken);
            // TODO Proper delays and exit handling
            await Task.Delay(3000, stoppingToken);
        }
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
        await configurationGate.WaitAsync(ct);
        try {
            if (stopping) return;
            logger.LogInformation("Regenerating sing-box configuration");
            var configuration = await globalConfigurationService.GetAsync(ct);
            var protocols = configuration.Protocols;
            using var session = documentStore.OpenAsyncSession();
            var clients = await session.Query<EntityClient>().ToListAsync(ct);

            var config = new SingBoxConfig();
            config.Log.Level = "debug";
            foreach (var contributor in contributors) await contributor.ContributeAsync(config, protocols, clients);
            var context = new SingBoxContext(config, clients, protocols, configuration.Transports);
            foreach (var extension in extensions) await extension.ConfigureAsync(context, ct);

            var configJson = JsonSerializer.Serialize(config, SerializerOptions);
            await singBoxProcessManager.ApplyConfigAsync(configJson);
            foreach (var extension in extensions) await extension.ApplyAsync(context, ct);
        }
        finally {
            configurationGate.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        stopping = true;
        globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        managedCertificateService.CertificatesChanged -= OnCertificatesChanged;
        await configurationGate.WaitAsync(CancellationToken.None);
        configurationGate.Release();
        try {
            await base.StopAsync(cancellationToken);
        }
        finally {
            singBoxProcessManager.Stop();
        }
    }

    public override void Dispose() {
        globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        managedCertificateService.CertificatesChanged -= OnCertificatesChanged;
        singBoxProcessManager.Dispose();
        base.Dispose();
    }
}