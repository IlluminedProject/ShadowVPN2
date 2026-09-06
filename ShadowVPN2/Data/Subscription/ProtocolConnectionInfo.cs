using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hysteria2ConnectionInfo), typeDiscriminator: "hysteria2")]
[JsonDerivedType(typeof(WireGuardConnectionInfo), "wireguard")]
public abstract class ProtocolConnectionInfo {
}