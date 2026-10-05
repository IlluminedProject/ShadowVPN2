namespace ShadowVPN2.Contracts.Auth;

public sealed class ExternalLoginProviderResponse {
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public bool UseDeviceFlow { get; init; }
}