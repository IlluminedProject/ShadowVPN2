using System.Text.Json.Serialization;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Entities;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FreeTurnTransportSettings), "free-turn")]
public abstract class TransportSettings {
    public Guid Id { get; set; }

    public required Guid ProtocolId { get; set; }

    public bool Enabled { get; set; } = true;

    public required PortBinding ListenPort { get; set; }
}