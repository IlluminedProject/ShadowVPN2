using System.Text.Json.Serialization;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Protocols;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Hysteria2ProtocolSettingsDto), "hysteria2")]
[JsonDerivedType(typeof(AwgProtocolSettingsDto), "wireguard")]
[JsonDerivedType(typeof(NaiveProxyProtocolSettingsDto), "naive")]
public abstract class ProtocolSettingsDto {
    public Guid Id { get; set; }
    public int ListenPort { get; set; }
    public bool Enabled { get; set; }
    public HostName? MainDomain { get; set; }
}