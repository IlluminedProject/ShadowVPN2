namespace ShadowVPN2.Contracts.Protocols;

public sealed class Hysteria2ProtocolSettingsDto : ProtocolSettingsDto {
    public string ObfsType { get; set; } = "salamander";
    public string ObfsPassword { get; set; } = string.Empty;
    public bool HasTlsCertificate { get; set; }
}