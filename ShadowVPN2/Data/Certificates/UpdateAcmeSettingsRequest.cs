namespace ShadowVPN2.Data.Certificates;

public sealed class UpdateAcmeSettingsRequest {
    public bool Enabled { get; set; }
    public string Email { get; set; } = "";
    public bool UseStaging { get; set; }
    public int HttpPort { get; set; } = 80;
    public int RenewalThresholdDays { get; set; } = 30;
}