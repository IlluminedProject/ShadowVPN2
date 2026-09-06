using System.Text.Json.Serialization;

namespace ShadowVPN2.Entities;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Hysteria2GlobalSettings), "hysteria2")]
[JsonDerivedType(typeof(WireGuardAmneziaGlobalSettings), "wireguard")]
public abstract class ProtocolGlobalSettings {
    public abstract string Protocol { get; }

    public bool Enabled { get; set; }

    public string? MainDomain { get; set; }
}