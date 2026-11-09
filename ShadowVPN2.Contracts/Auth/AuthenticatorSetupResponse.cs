namespace ShadowVPN2.Contracts.Auth;

public sealed class AuthenticatorSetupResponse {
    public required string SharedKey { get; init; }
    public required string Uri { get; init; }
}