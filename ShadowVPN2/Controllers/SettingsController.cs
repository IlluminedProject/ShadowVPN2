using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AppPermissions.Settings.View)]
public class SettingsController(SettingsService settingsService) : ControllerBase
{
    [HttpGet("auth")]
    public async Task<AuthSettingsResponse> GetAuthSettings()
    {
        var config = await settingsService.GetConfigurationAsync();
        var oidc = config.Providers.OfType<OidcAuthProvider>().FirstOrDefault();
        var local = config.Providers.OfType<LocalAuthProvider>().FirstOrDefault();

        return new AuthSettingsResponse
        {
            EnableLocalLogin = local != null,
            EnableOidc = oidc != null,
            OidcSettings = oidc == null ? null : new OidcAuthSettings
            {
                DisplayName = oidc.DisplayName,
                Authority = oidc.Authority,
                ClientId = oidc.ClientId,
                ClientSecret = oidc.ClientSecret
            }
        };
    }

    [HttpPost("auth")]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task SaveAuthSettings([FromBody] UpdateAuthSettingsRequest request)
    {
        await settingsService.SaveAuthSettingsAsync(request);
    }

    [HttpPost("auth/test-oidc")]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task<bool> TestOidcConnection([FromBody] string authority)
    {
        return await settingsService.TestOidcConnectionAsync(authority);
    }
}
