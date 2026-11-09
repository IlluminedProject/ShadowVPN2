using System.Buffers.Text;
using Microsoft.AspNetCore.Identity;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class AccountApplicationService(
    FrontendUserContext userContext,
    UserManager<ApplicationUser> userManager) : IAccountApplicationService {
    public async Task<AccountProfileResponse> GetProfileAsync() {
        return await GetProfileAsync(await userContext.RequireUserAsync());
    }

    public async Task<IReadOnlyList<PasskeyResponse>> GetPasskeysAsync() {
        return await GetPasskeysAsync(await userContext.RequireUserAsync());
    }

    public async Task<AuthenticatorSetupResponse> GetAuthenticatorSetupAsync() {
        return await GetAuthenticatorSetupAsync(await userContext.RequireUserAsync());
    }

    public async Task<AccountProfileResponse> GetProfileAsync(ApplicationUser user,
        bool isTwoFactorClientRemembered = false) {
        return new AccountProfileResponse {
            UserName = await userManager.GetUserNameAsync(user) ?? string.Empty,
            Email = await userManager.GetEmailAsync(user),
            PhoneNumber = await userManager.GetPhoneNumberAsync(user),
            HasPassword = await userManager.HasPasswordAsync(user),
            TwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user),
            HasAuthenticator = await userManager.GetAuthenticatorKeyAsync(user) is not null,
            RecoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user),
            IsTwoFactorClientRemembered = isTwoFactorClientRemembered
        };
    }

    public async Task<IReadOnlyList<PasskeyResponse>> GetPasskeysAsync(ApplicationUser user) {
        return (await userManager.GetPasskeysAsync(user)).Select(passkey => new PasskeyResponse {
            Id = Base64Url.EncodeToString(passkey.CredentialId), Name = passkey.Name
        }).ToArray();
    }

    public async Task<AuthenticatorSetupResponse> GetAuthenticatorSetupAsync(ApplicationUser user) {
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key)) {
            var result = await userManager.ResetAuthenticatorKeyAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Unable to reset authenticator key");
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("Authenticator key is unavailable");
        var email = await userManager.GetEmailAsync(user) ?? user.UserName ?? "user";
        return new AuthenticatorSetupResponse {
            SharedKey = key,
            Uri = $"otpauth://totp/ShadowVPN2:{Uri.EscapeDataString(email)}?secret={key}&issuer=ShadowVPN2&digits=6"
        };
    }
}