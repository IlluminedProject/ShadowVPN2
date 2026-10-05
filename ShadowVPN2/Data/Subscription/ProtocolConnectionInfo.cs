using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hysteria2ConnectionInfo), "hysteria2")]
[JsonDerivedType(typeof(WireGuardConnectionInfo), "wireguard")]
[JsonDerivedType(typeof(NaiveProxyConnectionInfo), "naive")]
public abstract class ProtocolConnectionInfo {
}