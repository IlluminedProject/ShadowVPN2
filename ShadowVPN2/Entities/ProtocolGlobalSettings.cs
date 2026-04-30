using System.Text.Json.Serialization;

namespace ShadowVPN2.Entities;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Hysteria2GlobalSettings), "hysteria2")]
public abstract class ProtocolGlobalSettings
{
    public string? Id { get; set; }

    public abstract string Protocol { get; }

    public bool Enabled { get; set; }
}