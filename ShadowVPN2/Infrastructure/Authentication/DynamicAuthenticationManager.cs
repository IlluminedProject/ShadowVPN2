using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using ShadowVPN2.Entities.Auth;

namespace ShadowVPN2.Infrastructure.Authentication;

public class DynamicAuthenticationManager(
    IAuthenticationSchemeProvider schemeProvider,
    IOptionsMonitorCache<OpenIdConnectOptions> oidcOptionsCache,
    IEnumerable<IPostConfigureOptions<OpenIdConnectOptions>> oidcPostConfigurers,
    DynamicOpenIddictClientRegistrationStore clientRegistrationStore,
    ILogger<DynamicAuthenticationManager> logger) {
    public async Task AddOrUpdateOidcProviderAsync(OidcAuthProvider dbProvider) {
        var schemeName = dbProvider.SchemeName;
        logger.LogInformation("Adding or updating OIDC provider: {SchemeName} ({Authority})", schemeName,
            dbProvider.Authority);
        clientRegistrationStore.AddOrUpdate(dbProvider);

        // 1. Create options manually to avoid premature validation in OptionsFactory
        var options = new OpenIdConnectOptions {
            Authority = dbProvider.Authority,
            ClientId = dbProvider.ClientId,
            ClientSecret = dbProvider.ClientSecret,
            ResponseType = "code",
            SaveTokens = true,
            CallbackPath = $"/signin-{schemeName}",
        };

        options.Scope.Clear();
        var scopes = dbProvider.Scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ??
                     ["openid", "email", "profile"];
        foreach (var scope in scopes) {
            options.Scope.Add(scope);
        }

        // 2. Run post-configurers (this sets up NonceCookie, CorrelationCookie, DataProtection, etc.)
        foreach (var postConfigurer in oidcPostConfigurers) {
            postConfigurer.PostConfigure(schemeName, options);
        }

        // 3. Update cache
        oidcOptionsCache.TryRemove(schemeName);
        oidcOptionsCache.TryAdd(schemeName, options);
        logger.LogDebug("Added new OIDC options cache for {SchemeName}", schemeName);

        // 3. Register scheme in ASP.NET Core if it doesn't exist
        if (await schemeProvider.GetSchemeAsync(schemeName) == null) {
            logger.LogInformation("Registering new OIDC authentication scheme: {SchemeName}", schemeName);
            var scheme = new AuthenticationScheme(
                schemeName,
                dbProvider.DisplayName,
                typeof(OpenIdConnectHandler));

            schemeProvider.AddScheme(scheme);
        }
        else {
            logger.LogDebug("OIDC authentication scheme {SchemeName} is already registered", schemeName);
        }
    }

    public void RemoveOidcProvider(string schemeName) {
        logger.LogInformation("Removing OIDC provider: {SchemeName}", schemeName);
        oidcOptionsCache.TryRemove(schemeName);
        clientRegistrationStore.Remove(schemeName);
        schemeProvider.RemoveScheme(schemeName);
    }
}