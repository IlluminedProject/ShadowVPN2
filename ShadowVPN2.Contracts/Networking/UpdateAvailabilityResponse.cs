namespace ShadowVPN2.Contracts.Networking;

public sealed class UpdateAvailabilityResponse {
    public string? ImageReference { get; set; }
    public string? CurrentDigest { get; set; }
    public string? AvailableDigest { get; set; }
    public bool UpdateAvailable { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public string? Error { get; set; }
    public bool Enabled { get; set; }
}