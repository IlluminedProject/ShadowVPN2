using ShadowVPN2.Contracts.Auth;

namespace ShadowVPN2.Contracts.Application;

public interface IAccountApplicationService {
    Task<AccountProfileResponse> GetProfileAsync();
    Task<IReadOnlyList<PasskeyResponse>> GetPasskeysAsync();
    Task<AuthenticatorSetupResponse> GetAuthenticatorSetupAsync();
}