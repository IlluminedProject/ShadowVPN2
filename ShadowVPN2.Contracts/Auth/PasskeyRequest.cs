namespace ShadowVPN2.Contracts.Auth;

public sealed class PasskeyRequest {
    public required string CredentialJson { get; init; }
    public string? ReturnUrl { get; init; }
}