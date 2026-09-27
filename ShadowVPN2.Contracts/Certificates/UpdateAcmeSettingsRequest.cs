namespace ShadowVPN2.Contracts.Certificates;

public sealed class UpdateAcmeSettingsRequest {
    public bool Enabled { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool UseStaging { get; set; }
    public int HttpPort { get; set; } = 80;
    public int RenewalThresholdDays { get; set; } = 30;
}