using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Identities;
using Raven.Client.Documents.Session;
using ShadowVPN2.Data;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Extensions;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    SetupService setupService,
    IAsyncDocumentSession documentSession,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    SettingsService settingsService,
    IDocumentStore documentStore,
    ILogger<AuthController> logger) : ControllerBase {
    [HttpGet("options")]
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IResult> Logout([FromForm] string? returnUrl) {
        await signInManager.SignOutAsync();
        return Results.LocalRedirect(SafeReturnUrl(returnUrl));
    }

    [HttpPost("external/start")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IResult StartExternal([FromForm] string provider, [FromForm] string? returnUrl) {
        var callback = UriHelper.BuildRelative(Request.PathBase, "/api/auth/external/callback",
            QueryString.Create("returnUrl", SafeReturnUrl(returnUrl)));
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, callback);
        return TypedResults.Challenge(properties, [provider]);
    }

    [AllowAnonymous]
    [HttpGet("external/callback")]
    public async Task<IResult> ExternalCallback([FromQuery] string? returnUrl) {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
            return Results.Redirect("/Account/Login?error=external");

        var safeReturnUrl = SafeReturnUrl(returnUrl);
        var result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, true);
        if (result.Succeeded)
            return Results.LocalRedirect(safeReturnUrl);
        if (result.IsLockedOut)
            return Results.Redirect("/Account/Lockout");

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return Results.Redirect("/Account/Login?error=external-email");

        var user = await userManager.FindByEmailAsync(email);
        if (user is not null) {
            var loginResult = await userManager.AddLoginAsync(user,
                new UserLoginInfo(info.LoginProvider, info.ProviderKey, info.ProviderDisplayName));
            if (!loginResult.Succeeded)
                return Results.Redirect("/Account/Login?error=external");
            await signInManager.SignInAsync(user, false, info.LoginProvider);
            return Results.LocalRedirect(safeReturnUrl);
        }

        var configuration = await settingsService.GetConfigurationAsync();
        var isFirstAdmin = (await userManager.GetUsersInRoleAsync(AppRoles.Administrator)).Count == 0;
        if (!isFirstAdmin && !configuration.SelfRegistrationEnabled)
            return Results.Redirect("/Account/Login?error=registration-disabled");

        var userNumber = (int)await documentStore.Maintenance.SendAsync(
            new NextIdentityForOperation("UserNumbers"));
        user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, UserNumber = userNumber };
        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
            return Results.Redirect("/Account/Login?error=external");
        await userManager.AddToRoleAsync(user, isFirstAdmin ? AppRoles.Administrator : AppRoles.User);
        var addLoginResult = await userManager.AddLoginAsync(user,
            new UserLoginInfo(info.LoginProvider, info.ProviderKey, info.ProviderDisplayName));
        if (!addLoginResult.Succeeded)
            return Results.Redirect("/Account/Login?error=external");
        await signInManager.SignInAsync(user, false, info.LoginProvider);
        return Results.LocalRedirect(safeReturnUrl);
    }

    [HttpPost("passkey/request-options")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IResult> PasskeyRequestOptions([FromQuery] string? username) {
        var user = string.IsNullOrWhiteSpace(username) ? null : await userManager.FindByNameAsync(username);
        var json = await signInManager.MakePasskeyRequestOptionsAsync(user);
        return Results.Content(json, "application/json");
    }

    [Authorize]
    [HttpPost("passkey/creation-options")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> PasskeyCreationOptions() {
        var user = await userManager.GetRequiredUserAsync(User);
        var userId = await userManager.GetUserIdAsync(user);
        var userName = await userManager.GetUserNameAsync(user) ?? "User";
        var json = await signInManager.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity {
            Id = userId, Name = userName, DisplayName = userName
        });
        return Results.Content(json, "application/json");
    }

    [HttpPost("passkey/sign-in")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IResult> PasskeySignIn([FromForm] PasskeyRequest request) {
        var result = await signInManager.PasskeySignInAsync(request.CredentialJson);
        if (result.Succeeded)
            return Results.LocalRedirect(SafeReturnUrl(request.ReturnUrl));
        if (result.IsLockedOut)
            return Results.Redirect("/Account/Lockout");
        return Results.Redirect(
            $"/Account/Login?error=invalid&returnUrl={Uri.EscapeDataString(SafeReturnUrl(request.ReturnUrl))}");
    }

    [Authorize]
    [HttpPost("passkey/register")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> RegisterPasskey([FromForm] PasskeyCreationRequest request) {
        var user = await userManager.GetRequiredUserAsync(User);
        var result = await signInManager.PerformPasskeyAttestationAsync(request.CredentialJson);
        if (!result.Succeeded)
            return Results.BadRequest(new { error = result.Failure?.Message ?? "Passkey registration failed." });
        var addResult = await userManager.AddOrUpdatePasskeyAsync(user, result.Passkey!);
        return addResult.Succeeded ? Results.Ok() : Results.BadRequest(addResult.Errors);
    }

    [HttpGet("test-oidc")]
    [AllowAnonymous]
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