using System.Buffers.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<AccountController> logger) : ControllerBase {
    [HttpGet("profile")]
    public async Task<AccountProfileResponse> Profile() {
        var user = await RequiredUser();
        return new AccountProfileResponse {
            UserName = await userManager.GetUserNameAsync(user) ?? string.Empty,
            Email = await userManager.GetEmailAsync(user),
            PhoneNumber = await userManager.GetPhoneNumberAsync(user),
            HasPassword = await userManager.HasPasswordAsync(user),
            TwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user),
            HasAuthenticator = await userManager.GetAuthenticatorKeyAsync(user) is not null,
            RecoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user),
            IsTwoFactorClientRemembered = await signInManager.IsTwoFactorClientRememberedAsync(user)
        };
    }

    [HttpPost("profile")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> UpdateProfile([FromForm] ProfileRequest request) {
        var user = await RequiredUser();
        var result = await userManager.SetPhoneNumberAsync(user, request.PhoneNumber);
        return await Finish(result, user, "Profile updated.");
    }

    [HttpPost("email")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> UpdateEmail([FromForm] EmailRequest request) {
        var user = await RequiredUser();
        var emailResult = await userManager.SetEmailAsync(user, request.Email);
        if (!emailResult.Succeeded) return Results.BadRequest(emailResult.Errors);
        var nameResult = await userManager.SetUserNameAsync(user, request.Email);
        return await Finish(nameResult, user, "Email updated.");
    }

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> ChangePassword([FromForm] PasswordRequest request) {
        var user = await RequiredUser();
        var result =
            await userManager.ChangePasswordAsync(user, request.CurrentPassword ?? string.Empty, request.NewPassword);
        return await Finish(result, user, "Password updated.");
    }

    [HttpPost("password/set")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> SetPassword([FromForm] PasswordRequest request) {
        var user = await RequiredUser();
        var result = await userManager.AddPasswordAsync(user, request.NewPassword);
        return await Finish(result, user, "Password set.");
    }

    [HttpGet("2fa")]
    public async Task<AccountProfileResponse> TwoFactor() {
        return await Profile();
    }

    [HttpPost("2fa/disable")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> DisableTwoFactor() {
        var user = await RequiredUser();
        var result = await userManager.SetTwoFactorEnabledAsync(user, false);
        return await Finish(result, user, "Two-factor authentication disabled.");
    }

    [HttpPost("2fa/reset")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> ResetAuthenticator() {
        var user = await RequiredUser();
        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await signInManager.RefreshSignInAsync(user);
        return Results.Redirect("/Account/Manage/EnableAuthenticator?message=Authenticator+key+reset.");
    }

    [HttpGet("2fa/setup")]
    public async Task<IResult> AuthenticatorSetup() {
        var user = await RequiredUser();
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key)) {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var email = await userManager.GetEmailAsync(user) ?? user.UserName ?? "user";
        return Results.Ok(new {
            SharedKey = key,
            Uri = $"otpauth://totp/ShadowVPN2:{Uri.EscapeDataString(email)}?secret={key}&issuer=ShadowVPN2&digits=6"
        });
    }

    [HttpPost("2fa/enable")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> EnableTwoFactor([FromForm] AuthenticatorCodeRequest request) {
        var user = await RequiredUser();
        var valid = await userManager.VerifyTwoFactorTokenAsync(user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            request.Code.Replace(" ", string.Empty).Replace("-", string.Empty));
        if (!valid) return Results.BadRequest(new[] { new { Description = "Invalid verification code." } });
        await userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Results.Ok(new { RecoveryCodes = codes });
    }

    [HttpPost("2fa/recovery-codes")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> GenerateRecoveryCodes() {
        var user = await RequiredUser();
        return Results.Ok(new { RecoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) });
    }

    [HttpPost("2fa/forget-browser")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> ForgetTwoFactorBrowser() {
        await signInManager.ForgetTwoFactorClientAsync();
        return Results.Redirect("/Account/Manage/TwoFactorAuthentication?message=Browser+forgotten.");
    }

    [HttpGet("passkeys")]
    public async Task<IReadOnlyList<PasskeyResponse>> Passkeys() {
        var user = await RequiredUser();
        return (await userManager.GetPasskeysAsync(user)).Select(passkey => new PasskeyResponse {
            Id = Base64Url.EncodeToString(passkey.CredentialId), Name = passkey.Name
        }).ToArray();
    }

    [HttpPost("passkeys/rename")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> RenamePasskey([FromForm] PasskeyRenameRequest request) {
        var user = await RequiredUser();
        var id = Base64Url.DecodeFromChars(request.Id);
        var passkey = await userManager.GetPasskeyAsync(user, id);
        if (passkey is null) return Results.NotFound();
        passkey.Name = request.Name;
        var result = await userManager.AddOrUpdatePasskeyAsync(user, passkey);
        return result.Succeeded
            ? Results.Redirect("/Account/Manage/Passkeys?message=Passkey+updated.")
            : Results.BadRequest(result.Errors);
    }

    [HttpPost("passkeys/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> DeletePasskey([FromForm] PasskeyDeleteRequest request) {
        var result = await userManager.RemovePasskeyAsync(await RequiredUser(), Base64Url.DecodeFromChars(request.Id));
        return result.Succeeded
            ? Results.Redirect("/Account/Manage/Passkeys?message=Passkey+deleted.")
            : Results.BadRequest(result.Errors);
    }

    [HttpGet("personal-data")]
    public async Task<IResult> PersonalData() {
        var user = await RequiredUser();
        var data = new Dictionary<string, string?>();
        foreach (var property in typeof(ApplicationUser).GetProperties()
                     .Where(x => Attribute.IsDefined(x, typeof(PersonalDataAttribute))))
            data[property.Name] = property.GetValue(user)?.ToString();
        foreach (var login in await userManager.GetLoginsAsync(user))
            data[$"{login.LoginProvider} login"] = login.ProviderKey;
        return Results.File(JsonSerializer.SerializeToUtf8Bytes(data), "application/json", "PersonalData.json");
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IResult> Delete([FromForm] DeleteAccountRequest request) {
        var user = await RequiredUser();
        if (await userManager.HasPasswordAsync(user) &&
            !await userManager.CheckPasswordAsync(user, request.Password ?? string.Empty))
            return Results.BadRequest(new[] { new { Description = "Incorrect password." } });
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded) return Results.BadRequest(result.Errors);
        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/");
    }

    private async Task<ApplicationUser> RequiredUser() {
        return await userManager.GetUserAsync(User) ?? throw new UnauthorizedAccessException();
    }

    private async Task<IResult> Finish(IdentityResult result, ApplicationUser user, string message) {
        if (!result.Succeeded) return Results.BadRequest(result.Errors);
        await signInManager.RefreshSignInAsync(user);
        logger.LogInformation("{Message}", message);
        return Results.Redirect($"/Account/Manage?message={Uri.EscapeDataString(message)}");
    }
}