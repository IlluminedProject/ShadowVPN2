namespace ShadowVPN2.Contracts.Auth;

public sealed class RecoveryCodeLoginRequest {
    public required string RecoveryCode { get; init; }
    public string? ReturnUrl { get; init; }
}