using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Protocols;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FreeTurnTransportSettingsDto), "free-turn")]
public abstract class TransportSettingsDto {
    public Guid Id { get; set; }
    public Guid ProtocolId { get; set; }
    public bool Enabled { get; set; } = true;
    public int ListenPort { get; set; }
}