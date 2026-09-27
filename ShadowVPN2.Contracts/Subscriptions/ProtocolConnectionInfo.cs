using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Subscriptions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hysteria2ConnectionInfo), "hysteria2")]
[JsonDerivedType(typeof(WireGuardConnectionInfo), "wireguard")]
public abstract class ProtocolConnectionInfo;