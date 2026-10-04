namespace ShadowVPN2.Contracts.Auth;

public sealed class PasskeyCreationRequest {
    public required string CredentialJson { get; init; }
}