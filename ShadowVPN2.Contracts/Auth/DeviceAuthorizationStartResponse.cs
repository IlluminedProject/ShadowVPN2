namespace ShadowVPN2.Contracts.Auth;

public sealed class DeviceAuthorizationStartResponse {
    public string TransactionId { get; init; } = string.Empty;
    public string VerificationUri { get; init; } = string.Empty;
    public string? VerificationUriComplete { get; init; }
    public string UserCode { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
    public int Interval { get; init; }
}