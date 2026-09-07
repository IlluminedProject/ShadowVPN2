namespace ShadowVPN2.Entities;

public sealed class AcmeSettings {
    public bool Enabled { get; set; }
    public string Email { get; set; } = "";
    public bool UseStaging { get; set; }
    public int HttpPort { get; set; } = 80;
    public int RenewalThresholdDays { get; set; } = 30;
}