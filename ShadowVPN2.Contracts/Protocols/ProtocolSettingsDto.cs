using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Protocols;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Hysteria2ProtocolSettingsDto), "hysteria2")]
[JsonDerivedType(typeof(AwgProtocolSettingsDto), "wireguard")]
public abstract class ProtocolSettingsDto {
    public Guid Id { get; set; }
    public int ListenPort { get; set; }
    public bool Enabled { get; set; }
    public string? MainDomain { get; set; }
}