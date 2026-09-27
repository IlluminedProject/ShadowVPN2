using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Raven.Client.Documents.Session;
using ShadowVPN2.Data;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController(
    SetupService setupService,
    IAsyncDocumentSession documentSession,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ILogger<AuthController> logger) : ControllerBase {
    [HttpGet("options")]
    public async Task<AuthOptionsResponse> GetOptions(CancellationToken cancellationToken) {
        var configuration = await documentSession.LoadAsync<EntityGlobalConfiguration>(
            "GlobalConfiguration", cancellationToken);
        var oidc = configuration?.Providers.OfType<OidcAuthProvider>().FirstOrDefault();
        var local = configuration?.Providers.OfType<LocalAuthProvider>().FirstOrDefault();
        var externalProviders = (await signInManager.GetExternalAuthenticationSchemesAsync())
            .Select(provider => new ExternalLoginProviderResponse {
                Name = provider.Name,
                DisplayName = provider.DisplayName ?? provider.Name
            })
            .ToArray();

        return new AuthOptionsResponse {
            LocalLoginEnabled = local?.IsEnabled == true,
            DeviceFlowEnabled = oidc?.IsEnabled == true && oidc.DeviceFlowEnabled,
            ExternalProviders = externalProviders
        };
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> Login([FromForm] LoginRequest request) {
        if (!Uri.TryCreate(request.ReturnUrl ?? "/", UriKind.Relative, out _)) {
            request = new LoginRequest {
                Email = request.Email,
                Password = request.Password,
                RememberMe = request.RememberMe,
                ReturnUrl = "/"
            };
        }

        var result = await signInManager.PasswordSignInAsync(
            request.Email, request.Password, request.RememberMe, false);

        if (result.Succeeded) {
            logger.LogInformation("User logged in");
            return Results.LocalRedirect(SafeReturnUrl(request.ReturnUrl));
        }

        if (result.RequiresTwoFactor) {
            return Results.Redirect(
                $"/Account/LoginWith2fa?returnUrl={Uri.EscapeDataString(SafeReturnUrl(request.ReturnUrl))}&rememberMe={request.RememberMe}");
        }

        if (result.IsLockedOut)
            return Results.Redirect("/Account/Lockout");

        return Results.Redirect(
            $"/Account/Login?error=invalid&returnUrl={Uri.EscapeDataString(SafeReturnUrl(request.ReturnUrl))}");
    }

    [HttpPost("2fa")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> TwoFactor([FromForm] TwoFactorLoginRequest request) {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return Results.Redirect("/Account/Login?error=expired");

        var code = request.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            code, request.RememberMe, request.RememberMachine);
        var userId = await userManager.GetUserIdAsync(user);

        if (result.Succeeded) {
            logger.LogInformation("User with ID '{UserId}' logged in with 2fa", userId);
            return Results.LocalRedirect(SafeReturnUrl(request.ReturnUrl));
        }

        if (result.IsLockedOut)
            return Results.Redirect("/Account/Lockout");

        return Results.Redirect(
            $"/Account/LoginWith2fa?error=invalid&returnUrl={Uri.EscapeDataString(SafeReturnUrl(request.ReturnUrl))}&rememberMe={request.RememberMe}");
    }

    [HttpPost("recovery-code")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> RecoveryCode([FromForm] RecoveryCodeLoginRequest request) {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return Results.Redirect("/Account/Login?error=expired");

        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(
            request.RecoveryCode.Replace(" ", string.Empty));
        var userId = await userManager.GetUserIdAsync(user);

        if (result.Succeeded) {
            logger.LogInformation("User with ID '{UserId}' logged in with a recovery code", userId);
            return Results.LocalRedirect(SafeReturnUrl(request.ReturnUrl));
        }

        if (result.IsLockedOut)
            return Results.Redirect("/Account/Lockout");

        return Results.Redirect(
            $"/Account/LoginWithRecoveryCode?error=invalid&returnUrl={Uri.EscapeDataString(SafeReturnUrl(request.ReturnUrl))}");
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> Logout([FromForm] string? returnUrl) {
        await signInManager.SignOutAsync();
        return Results.LocalRedirect(SafeReturnUrl(returnUrl));
    }

    [HttpGet("test-oidc")]
    public async Task<bool> TestOidc([FromQuery] string authority) {
        return await setupService.TestOidcConnectionAsync(authority);
    }

    private static string SafeReturnUrl(string? returnUrl) {
        return !string.IsNullOrWhiteSpace(returnUrl) && Uri.TryCreate(returnUrl, UriKind.Relative, out _) &&
               returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";
    }
}