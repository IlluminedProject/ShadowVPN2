using System;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;

namespace ShadowVPN2.Infrastructure.Authentication;

public class DynamicAuthInitializerService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DynamicAuthInitializerService> _logger;
    private IDisposable? _documentSubscription;
    private readonly HashSet<string> _activeOidcSchemes = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public DynamicAuthInitializerService(
        IServiceScopeFactory scopeFactory,
        ILogger<DynamicAuthInitializerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting dynamic authentication initialization");

        await LoadAndApplyConfigurationAsync(cancellationToken);

        // Subscribe to changes
        try
        {
            var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            
            _documentSubscription = store.Changes()
                .ForDocument("GlobalConfiguration")
                .Subscribe(new ActionObserver<DocumentChange>(change =>
                {
                    _logger.LogInformation("Detected changes in GlobalConfiguration. Reloading authentication providers");
                    // Run in background to avoid blocking the RavenDB websocket connection
                    _ = Task.Run(async () => await LoadAndApplyConfigurationAsync(CancellationToken.None));
                }));
                
            _logger.LogInformation("Successfully subscribed to GlobalConfiguration changes");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to GlobalConfiguration changes");
        }
    }

    private async Task LoadAndApplyConfigurationAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            var authManager = scope.ServiceProvider.GetRequiredService<DynamicAuthenticationManager>();

            using var session = store.OpenAsyncSession();
            var globalConfig = await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration", cancellationToken);

            var currentOidcSchemes = new HashSet<string>();

            if (globalConfig?.Providers != null)
            {
                var enabledProviders = globalConfig.Providers.Where(p => p.IsEnabled).ToList();
                _logger.LogInformation("Found {Count} enabled authentication providers in database", enabledProviders.Count);

                foreach (var provider in enabledProviders)
                {
                    _logger.LogDebug("Registering scheme for provider: {DisplayName} ({Type})", provider.DisplayName, provider.GetType().Name);
                    await provider.RegisterSchemeAsync(authManager);
                    
                    if (provider is OidcAuthProvider oidc)
                    {
                        currentOidcSchemes.Add(oidc.SchemeName);
                    }
                }
            }
            else
            {
                _logger.LogInformation("No global configuration found or no providers configured");
            }

            // Remove disabled or deleted OIDC schemes
            var schemesToRemove = _activeOidcSchemes.Except(currentOidcSchemes).ToList();
            foreach (var scheme in schemesToRemove)
            {
                _logger.LogInformation("Removing disabled or deleted OIDC scheme: {Scheme}", scheme);
                authManager.RemoveOidcProvider(scheme);
            }

            // Update tracked schemes
            _activeOidcSchemes.Clear();
            foreach (var s in currentOidcSchemes)
            {
                _activeOidcSchemes.Add(s);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while loading dynamic authentication providers");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _documentSubscription?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _documentSubscription?.Dispose();
        _semaphore.Dispose();
    }
}
