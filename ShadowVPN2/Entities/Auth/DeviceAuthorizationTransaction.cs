namespace ShadowVPN2.Entities.Auth;

public sealed class DeviceAuthorizationTransaction {
    public string Id { get; set; } = string.Empty;
    public string ProviderScheme { get; set; } = string.Empty;
    public string ProtectedDeviceCode { get; set; } = string.Empty;
    public string CorrelationHash { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string VerificationUri { get; set; } = string.Empty;
    public string? VerificationUriComplete { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int PollInterval { get; set; }
    public DeviceAuthorizationStatus Status { get; set; } = DeviceAuthorizationStatus.Pending;
    public string? ProviderKey { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? Error { get; set; }
}