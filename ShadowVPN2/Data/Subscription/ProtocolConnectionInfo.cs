using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hysteria2ConnectionInfo), typeDiscriminator: "hysteria2")]
public abstract class ProtocolConnectionInfo
{
}