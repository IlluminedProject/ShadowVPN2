namespace ShadowVPN2.Contracts.Protocols;

public sealed class NaiveProxyProtocolSettingsDto : ProtocolSettingsDto, IListenPortSettings {
    public bool HasTlsCertificate { get; set; }
    public int ListenPort { get; set; }
}