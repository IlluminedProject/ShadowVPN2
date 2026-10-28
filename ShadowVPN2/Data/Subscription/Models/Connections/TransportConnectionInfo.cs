using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Connections;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FreeTurnConnectionInfo), "free-turn")]
public abstract class TransportConnectionInfo {
}