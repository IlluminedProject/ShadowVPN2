using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class GlobalConfigurationService : IHostedService, IDisposable
{
    private readonly ILogger<GlobalConfigurationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IDisposable? _documentSubscription;

    public GlobalConfigurationService(IServiceProvider serviceProvider, ILogger<GlobalConfigurationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void Dispose()
    {
        _documentSubscription?.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var documentStore = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        // 1. Data Migration for Protocols (if needed)
        try
        {
            using var session = documentStore.OpenAsyncSession();
            var oldProtocols = await session.Advanced
                .AsyncDocumentQuery<ProtocolGlobalSettings>(collectionName: "Protocols")
                .ToListAsync(cancellationToken);

            if (oldProtocols.Any())
            {
                _logger.LogInformation(
                    "Migrating {Count} legacy ProtocolGlobalSettings documents to GlobalConfiguration",
                    oldProtocols.Count);
                var config =
                    await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration", cancellationToken) ??
                    new EntityGlobalConfiguration();

                foreach (var p in oldProtocols)
                {
                    if (!config.Protocols.Any(x => x.Protocol == p.Protocol)) config.Protocols.Add(p);

                    var id = session.Advanced.GetDocumentId(p);
                    if (id != null) session.Delete(id);
                }

                await session.StoreAsync(config, "GlobalConfiguration", cancellationToken);
                await session.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully migrated ProtocolGlobalSettings");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ProtocolGlobalSettings migration");
        }

        // 2. Subscribe to GlobalConfiguration changes
        _documentSubscription = documentStore.Changes()
            .ForDocument("GlobalConfiguration")
            .Subscribe(new ActionObserver<DocumentChange>(change =>
            {
                _logger.LogInformation("GlobalConfiguration changed in database");
                // Emit event
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var config = await GetAsync(CancellationToken.None);
                        ConfigurationChanged?.Invoke(this, config);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load GlobalConfiguration for event");
                    }
                });
            }));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _documentSubscription?.Dispose();
        return Task.CompletedTask;
    }

    public event EventHandler<EntityGlobalConfiguration>? ConfigurationChanged;

    public async Task<EntityGlobalConfiguration> GetAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var documentStore = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        using var session = documentStore.OpenAsyncSession();
        var config = await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration", cancellationToken);
        return config ?? new EntityGlobalConfiguration();
    }

    public Task UpdateAsync(Action<EntityGlobalConfiguration> updateAction,
        CancellationToken cancellationToken = default)
    {
        return UpdateAsync(config =>
        {
            updateAction(config);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task UpdateAsync(Func<EntityGlobalConfiguration, Task> updateAction,
        CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var documentStore = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        using var session = documentStore.OpenAsyncSession();
        var config = await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration", cancellationToken);

        if (config == null)
        {
            config = new EntityGlobalConfiguration();
            await session.StoreAsync(config, "GlobalConfiguration", cancellationToken);
        }

        await updateAction(config);

        await session.SaveChangesAsync(cancellationToken);
    }
}