namespace ShadowVPN2.Contracts.Auth;

public sealed class AuthOptionsResponse {
    public bool LocalLoginEnabled { get; init; }
    public bool DeviceFlowEnabled { get; init; }
    public IReadOnlyList<ExternalLoginProviderResponse> ExternalProviders { get; init; } = [];
}

public sealed class ExternalLoginProviderResponse {
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
}