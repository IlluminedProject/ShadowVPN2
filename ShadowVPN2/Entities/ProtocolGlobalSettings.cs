using System.Text.Json.Serialization;

namespace ShadowVPN2.Entities;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Hysteria2GlobalSettings), "hysteria2")]
[JsonDerivedType(typeof(AwgGlobalSettings), "wireguard")]
public abstract class ProtocolGlobalSettings {
    public Guid Id { get; set; }

    public abstract string Protocol { get; }

    public abstract int ListenPort { get; set; }

    public bool Enabled { get; set; }

    public string? MainDomain { get; set; }
}