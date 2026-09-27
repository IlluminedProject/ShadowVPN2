namespace ShadowVPN2.Contracts.Auth;

public sealed class TwoFactorLoginRequest {
    public required string TwoFactorCode { get; init; }
    public bool RememberMachine { get; init; }
    public bool RememberMe { get; init; }
    public string? ReturnUrl { get; init; }
}