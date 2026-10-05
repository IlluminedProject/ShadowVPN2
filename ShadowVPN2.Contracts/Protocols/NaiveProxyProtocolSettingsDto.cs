namespace ShadowVPN2.Contracts.Protocols;

public sealed class NaiveProxyProtocolSettingsDto : ProtocolSettingsDto {
    public bool HasTlsCertificate { get; set; }
}