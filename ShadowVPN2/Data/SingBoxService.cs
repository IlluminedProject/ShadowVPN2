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
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class SingBoxService(
    ILogger<SingBoxService> logger,
    IDocumentStore documentStore,
    IEnumerable<ISingBoxConfigContributor> contributors,
    ProtocolSettingsService protocolSettingsService,
    GlobalConfigurationService globalConfigurationService,
    SingBoxProcessManager singBoxProcessManager)
    : BackgroundService
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public bool IsRunning => singBoxProcessManager.IsRunning;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SingBoxService starting");
        await RegenerateAndApplyConfigAsync(stoppingToken);

        // Subscribe to changes
        globalConfigurationService.ConfigurationChanged += OnConfigurationChanged;

        using var clientsSubscription = documentStore.Changes()
            .ForDocumentsInCollection<EntityClient>()
            .Subscribe(new ActionObserver<DocumentChange>(change =>
            {
                logger.LogInformation("Clients collection changed, regenerating sing-box config");
                _ = RegenerateAndApplyConfigAsync(CancellationToken.None);
            }));

        using var nodesSubscription = documentStore.Changes()
            .ForDocumentsInCollection<EntityClusterNode>()
            .Subscribe(new ActionObserver<DocumentChange>(change =>
            {
                logger.LogInformation("Nodes collection changed, regenerating sing-box config");
                _ = RegenerateAndApplyConfigAsync(CancellationToken.None);
            }));

        while (!stoppingToken.IsCancellationRequested)
        {
            singBoxProcessManager.Start();
            await singBoxProcessManager.WaitForExitAsync(stoppingToken);
            // TODO Proper delays and exit handling
            await Task.Delay(3000, stoppingToken);
        }
    }

    private void OnConfigurationChanged(object? sender, EntityGlobalConfiguration e)
    {
        logger.LogInformation("Global configuration changed, regenerating sing-box config");
        _ = RegenerateAndApplyConfigAsync(CancellationToken.None);
    }

    public async Task RegenerateAndApplyConfigAsync(CancellationToken ct)
    {
        logger.LogInformation("Regenerating sing-box configuration");

        var protocols = await protocolSettingsService.GetConfigurationAsync();

        using var session = documentStore.OpenAsyncSession();
        var clients = await session.Query<EntityClient>().ToListAsync(ct);

        var config = new SingBoxConfig();
        config.Log.Level = "debug";
        foreach (var contributor in contributors) await contributor.ContributeAsync(config, protocols, clients);

        var configJson = JsonSerializer.Serialize(config, SerializerOptions);
        await singBoxProcessManager.ApplyConfigAsync(configJson);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        singBoxProcessManager.Stop();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        singBoxProcessManager.Dispose();
        base.Dispose();
    }
}