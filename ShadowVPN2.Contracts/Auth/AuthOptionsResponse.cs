namespace ShadowVPN2.Contracts.Auth;

public sealed class AuthOptionsResponse {
    public bool LocalLoginEnabled { get; init; }
    public IReadOnlyList<ExternalLoginProviderResponse> ExternalProviders { get; init; } = [];
}