using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class GlobalConfigurationService(IServiceProvider serviceProvider, ILogger<GlobalConfigurationService> logger)
    : IHostedService, IDisposable {
    private IDisposable? _documentSubscription;

    public void Dispose() {
        _documentSubscription?.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken) {
        using var scope = serviceProvider.CreateScope();
        var documentStore = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        // Subscribe to GlobalConfiguration changes
        _documentSubscription = documentStore.Changes()
            .ForDocument("GlobalConfiguration")
            .Subscribe(new ActionObserver<DocumentChange>(change => {
                logger.LogInformation("GlobalConfiguration changed in database");
                // Emit event
                _ = Task.Run(async () => {
                    try {
                        var config = await GetAsync(CancellationToken.None);
                        ConfigurationChanged?.Invoke(this, config);
                    }
                    catch (Exception ex) {
                        logger.LogError(ex, "Failed to load GlobalConfiguration for event");
                    }
                });
            }));
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        _documentSubscription?.Dispose();
        return Task.CompletedTask;
    }

    public event EventHandler<EntityGlobalConfiguration>? ConfigurationChanged;

    public async Task<EntityGlobalConfiguration> GetAsync(CancellationToken cancellationToken = default) {
        using var scope = serviceProvider.CreateScope();
        var documentStore = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        using var session = documentStore.OpenAsyncSession();
        var config = await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration", cancellationToken);
        return config ?? new EntityGlobalConfiguration();
    }

    public Task UpdateAsync(Action<EntityGlobalConfiguration> updateAction,
        CancellationToken cancellationToken = default) {
        return UpdateAsync(config => {
            updateAction(config);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task UpdateAsync(Func<EntityGlobalConfiguration, Task> updateAction,
        CancellationToken cancellationToken = default) {
        using var scope = serviceProvider.CreateScope();
        var documentStore = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        using var session = documentStore.OpenAsyncSession();
        var config = await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration", cancellationToken);

        if (config == null) {
            config = new EntityGlobalConfiguration();
            await session.StoreAsync(config, "GlobalConfiguration", cancellationToken);
        }

        await updateAction(config);

        await session.SaveChangesAsync(cancellationToken);
    }
}