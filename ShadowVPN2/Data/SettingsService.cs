using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Infrastructure.Authentication;
using SessionOptions = Raven.Client.Documents.Session.SessionOptions;

namespace ShadowVPN2.Data;

public class SettingsService(
    IHttpClientFactory httpClientFactory,
    IDocumentStore documentStore,
    DynamicAuthenticationManager authManager,
    ILogger<SettingsService> logger)
{
    public async Task<EntityGlobalConfiguration> GetConfigurationAsync()
    {
        using var session = documentStore.OpenAsyncSession();
        var config = await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration");
        return config ?? new EntityGlobalConfiguration();
    }

    public async Task<bool> TestOidcConnectionAsync(string authority)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(authority)) return false;

            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var discoveryUrl = authority.TrimEnd('/') + "/.well-known/openid-configuration";
            logger.LogInformation("Testing OIDC discovery endpoint at {DiscoveryUrl}", discoveryUrl);
            var response = await client.GetAsync(discoveryUrl);

            logger.LogInformation("OIDC discovery endpoint returned {StatusCode}", response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to connect to OIDC authority {Authority}", authority);
            return false;
        }
    }

    public async Task SaveAuthSettingsAsync(UpdateAuthSettingsRequest request)
    {
        using var session = documentStore.OpenAsyncSession(new SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });

        var globalConfig = await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration");
        if (globalConfig == null)
        {
            globalConfig = new EntityGlobalConfiguration();
            await session.StoreAsync(globalConfig);
        }

        globalConfig.SelfRegistrationEnabled = request.SelfRegistrationEnabled;

        // 1. Handle Local Auth
        var local = globalConfig.Providers.OfType<LocalAuthProvider>().FirstOrDefault();
        if (local == null)
        {
            local = new LocalAuthProvider();
            globalConfig.Providers.Add(local);
        }
        local.IsEnabled = request.EnableLocalLogin;

        // 2. Handle OIDC Providers
        var existingOidcProviders = globalConfig.Providers.OfType<OidcAuthProvider>().ToList();
        var schemeName = "OIDC"; // Primary OIDC scheme

        var current = existingOidcProviders.FirstOrDefault(p => p.SchemeName == schemeName);

        if (request.EnableOidc && request.OidcSettings != null)
        {
            if (current == null)
            {
                current = new OidcAuthProvider { SchemeName = schemeName };
                globalConfig.Providers.Add(current);
            }

            current.DisplayName = request.OidcSettings.DisplayName;
            current.Authority = request.OidcSettings.Authority;
            current.ClientId = request.OidcSettings.ClientId;
            current.ClientSecret = request.OidcSettings.ClientSecret;
            current.IsEnabled = true;

            await authManager.AddOrUpdateOidcProviderAsync(current);
        }
        else if (current != null)
        {
            current.IsEnabled = false;
            authManager.RemoveOidcProvider(current.SchemeName);
        }

        // Remove any other OIDC providers that might have been added manually
        foreach (var p in existingOidcProviders.Where(p => p.SchemeName != schemeName))
        {
            authManager.RemoveOidcProvider(p.SchemeName);
            globalConfig.Providers.Remove(p);
        }

        await session.SaveChangesAsync();
        logger.LogInformation("Auth settings saved. Local: {Local}, OIDC: {Oidc}", request.EnableLocalLogin, request.EnableOidc);
    }
}
