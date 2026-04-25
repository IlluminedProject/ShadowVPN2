using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;

namespace ShadowVPN2.Infrastructure.Authentication;

public class DynamicAuthenticationManager
{
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly IOptionsMonitorCache<OpenIdConnectOptions> _oidcOptionsCache;
    private readonly ILogger<DynamicAuthenticationManager> _logger;

    public DynamicAuthenticationManager(
        IAuthenticationSchemeProvider schemeProvider,
        IOptionsMonitorCache<OpenIdConnectOptions> oidcOptionsCache,
        ILogger<DynamicAuthenticationManager> logger)
    {
        _schemeProvider = schemeProvider;
        _oidcOptionsCache = oidcOptionsCache;
        _logger = logger;
    }

    public async Task AddOrUpdateOidcProviderAsync(OidcAuthProvider dbProvider)
    {
        var schemeName = dbProvider.SchemeName;
        _logger.LogInformation("Adding or updating OIDC provider: {SchemeName} ({Authority})", schemeName, dbProvider.Authority);

        // 1. Clear old cache if it exists
        if (_oidcOptionsCache.TryRemove(schemeName))
        {
            _logger.LogDebug("Removed existing OIDC options cache for {SchemeName}", schemeName);
        }

        // 2. Add new settings to cache
        _oidcOptionsCache.TryAdd(schemeName, new OpenIdConnectOptions
        {
            Authority = dbProvider.Authority,
            ClientId = dbProvider.ClientId,
            ClientSecret = dbProvider.ClientSecret,
            ResponseType = "code",
            SaveTokens = true,
            CallbackPath = $"/signin-{schemeName}",
            // Mapping default claims if necessary
        });
        _logger.LogDebug("Added new OIDC options cache for {SchemeName}", schemeName);

        // 3. Register scheme in ASP.NET Core if it doesn't exist
        if (await _schemeProvider.GetSchemeAsync(schemeName) == null)
        {
            _logger.LogInformation("Registering new OIDC authentication scheme: {SchemeName}", schemeName);
            var scheme = new AuthenticationScheme(
                schemeName,
                dbProvider.DisplayName,
                typeof(OpenIdConnectHandler));

            _schemeProvider.AddScheme(scheme);
        }
        else
        {
            _logger.LogDebug("OIDC authentication scheme {SchemeName} is already registered", schemeName);
        }
    }

    public void RemoveOidcProvider(string schemeName)
    {
        _logger.LogInformation("Removing OIDC provider: {SchemeName}", schemeName);
        _oidcOptionsCache.TryRemove(schemeName);
        _schemeProvider.RemoveScheme(schemeName);
    }
}
