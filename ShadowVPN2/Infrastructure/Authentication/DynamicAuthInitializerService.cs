using ShadowVPN2.Data;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;

namespace ShadowVPN2.Infrastructure.Authentication;

public class DynamicAuthInitializerService(
    GlobalConfigurationService globalConfigurationService,
    DynamicAuthenticationManager dynamicAuthenticationManager,
    ILogger<DynamicAuthInitializerService> logger)
    : IHostedService, IDisposable
{
    private readonly HashSet<string> _activeOidcSchemes = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public void Dispose()
    {
        globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        _semaphore.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting dynamic authentication initialization");

        await LoadAndApplyConfigurationAsync(cancellationToken);
        globalConfigurationService.ConfigurationChanged += OnConfigurationChanged;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        globalConfigurationService.ConfigurationChanged -= OnConfigurationChanged;
        return Task.CompletedTask;
    }

    private void OnConfigurationChanged(object? sender, EntityGlobalConfiguration config)
    {
        logger.LogInformation("Detected changes in GlobalConfiguration. Reloading authentication providers");
        // Run in background to avoid blocking the caller
        _ = Task.Run(async () => await LoadAndApplyConfigurationAsync(CancellationToken.None));
    }

    private async Task LoadAndApplyConfigurationAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var globalConfig = await globalConfigurationService.GetAsync(cancellationToken);

            var currentOidcSchemes = new HashSet<string>();

            if (globalConfig?.Providers != null)
            {
                var enabledProviders = globalConfig.Providers.Where(p => p.IsEnabled).ToList();
                logger.LogInformation("Found {Count} enabled authentication providers in database",
                    enabledProviders.Count);

                foreach (var provider in enabledProviders)
                {
                    logger.LogDebug("Registering scheme for provider: {DisplayName} ({Type})", provider.DisplayName,
                        provider.GetType().Name);
                    await provider.RegisterSchemeAsync(dynamicAuthenticationManager);

                    if (provider is OidcAuthProvider oidc)
                    {
                        currentOidcSchemes.Add(oidc.SchemeName);
                    }
                }
            }
            else
            {
                logger.LogInformation("No global configuration found or no providers configured");
            }

            // Remove disabled or deleted OIDC schemes
            var schemesToRemove = _activeOidcSchemes.Except(currentOidcSchemes).ToList();
            foreach (var scheme in schemesToRemove)
            {
                logger.LogInformation("Removing disabled or deleted OIDC scheme: {Scheme}", scheme);
                dynamicAuthenticationManager.RemoveOidcProvider(scheme);
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
            logger.LogError(ex, "An error occurred while loading dynamic authentication providers");
        }
        finally
        {
            _semaphore.Release();
        }
    }
}