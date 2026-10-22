using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class SettingsService(
    IHttpClientFactory httpClientFactory,
    GlobalConfigurationService globalConfigService,
    DynamicAuthenticationManager authManager,
    ILogger<SettingsService> logger) {
    public async Task<EntityGlobalConfiguration> GetConfigurationAsync() {
        return await globalConfigService.GetAsync();
    }

    public async Task<bool> TestOidcConnectionAsync(string authority) {
        if (string.IsNullOrWhiteSpace(authority)) return false;
        var discoveryUrl = authority.TrimEnd('/') + "/.well-known/openid-configuration";
        if (!Uri.TryCreate(discoveryUrl, UriKind.Absolute, out var discoveryUri) ||
            discoveryUri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(discoveryUri.UserInfo) ||
            !string.IsNullOrEmpty(discoveryUri.Query) || !string.IsNullOrEmpty(discoveryUri.Fragment))
            return false;

        try {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            logger.LogInformation("Testing OIDC discovery endpoint at {Authority}",
                discoveryUri.GetLeftPart(UriPartial.Authority));
            using var response = await client.GetAsync(discoveryUri);

            logger.LogInformation("OIDC discovery endpoint returned {StatusCode}", response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
            logger.LogWarning(ex, "Failed to connect to OIDC authority {Authority}",
                discoveryUri.GetLeftPart(UriPartial.Authority));
            return false;
        }
    }

    public async Task SaveAuthSettingsAsync(UpdateAuthSettingsRequest request) {
        await globalConfigService.UpdateAsync(async globalConfig => {
            globalConfig.SelfRegistrationEnabled = request.SelfRegistrationEnabled;

            // 1. Handle Local Auth
            var local = globalConfig.Providers.OfType<LocalAuthProvider>().FirstOrDefault();
            if (local == null) {
                local = new LocalAuthProvider();
                globalConfig.Providers.Add(local);
            }

            local.IsEnabled = request.EnableLocalLogin;

            // 2. Handle OIDC Providers
            var existingOidcProviders = globalConfig.Providers.OfType<OidcAuthProvider>().ToList();
            var schemeName = "OIDC"; // Primary OIDC scheme

            var current = existingOidcProviders.FirstOrDefault(p => p.SchemeName == schemeName);

            if (request.EnableOidc && request.OidcSettings != null) {
                if (current == null) {
                    current = new OidcAuthProvider { SchemeName = schemeName };
                    globalConfig.Providers.Add(current);
                }

                current.DisplayName = request.OidcSettings.DisplayName;
                current.Authority = request.OidcSettings.Authority;
                current.ClientId = request.OidcSettings.ClientId;
                current.Scopes = request.OidcSettings.Scopes;
                current.DeviceFlowMode = request.OidcSettings.DeviceFlowMode;
                if (!string.IsNullOrWhiteSpace(request.OidcSettings.ClientSecret))
                    current.ClientSecret = request.OidcSettings.ClientSecret;
                current.IsEnabled = true;

                await authManager.AddOrUpdateOidcProviderAsync(current);
            }
            else if (current != null) {
                current.IsEnabled = false;
                authManager.RemoveOidcProvider(current.SchemeName);
            }

            // Remove any other OIDC providers that might have been added manually
            foreach (var p in existingOidcProviders.Where(p => p.SchemeName != schemeName)) {
                authManager.RemoveOidcProvider(p.SchemeName);
                globalConfig.Providers.Remove(p);
            }
        });

        logger.LogInformation("Auth settings saved. Local: {Local}, OIDC: {Oidc}", request.EnableLocalLogin,
            request.EnableOidc);
    }
}