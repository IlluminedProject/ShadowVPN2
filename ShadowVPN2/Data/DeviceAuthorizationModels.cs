namespace ShadowVPN2.Data;

public sealed class DeviceAuthorizationStartResponse {
    public string TransactionId { get; init; } = string.Empty;
    public string VerificationUri { get; init; } = string.Empty;
    public string? VerificationUriComplete { get; init; }
    public string UserCode { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
    public int Interval { get; init; }
}

public sealed class DeviceAuthorizationPollResponse {
    public string Status { get; init; } = string.Empty;
    public int RetryAfter { get; init; }
    public string? Error { get; init; }
}

public sealed class DeviceAuthorizationCompleteResponse {
    public string Status { get; init; } = string.Empty;
    public string? Error { get; init; }
}