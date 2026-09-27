using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Subscriptions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FreeTurnConnectionInfo), "free-turn")]
public abstract class TransportConnectionInfo;