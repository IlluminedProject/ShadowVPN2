namespace ShadowVPN2.Contracts.Auth;

public sealed class DeviceAuthorizationPollResponse {
    public string Status { get; init; } = string.Empty;
    public int RetryAfter { get; init; }
    public string? Error { get; init; }
}