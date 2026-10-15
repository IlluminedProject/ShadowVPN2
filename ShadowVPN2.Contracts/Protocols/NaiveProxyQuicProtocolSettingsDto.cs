using System.ComponentModel.DataAnnotations;

namespace ShadowVPN2.Contracts.Protocols;

public sealed class NaiveProxyQuicProtocolSettingsDto : ProtocolSettingsDto, IListenPortSettings {
    public bool HasTlsCertificate { get; set; }
    [Range(1, 65535)] public int ListenPort { get; set; }
}