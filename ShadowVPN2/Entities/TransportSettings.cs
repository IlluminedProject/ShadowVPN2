using System.Text.Json.Serialization;

namespace ShadowVPN2.Entities;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FreeTurnTransportSettings), "free-turn")]
public abstract class TransportSettings {
    public Guid Id { get; set; }

    public required Guid ProtocolId { get; set; }

    public bool Enabled { get; set; } = true;

    public required int ListenPort { get; set; }
}