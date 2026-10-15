using System.Text.Json.Serialization;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Entities;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Hysteria2GlobalSettings), "hysteria2")]
[JsonDerivedType(typeof(AwgGlobalSettings), "wireguard")]
[JsonDerivedType(typeof(NaiveProxyGlobalSettings), "naive")]
public abstract class ProtocolGlobalSettings {
    public Guid Id { get; set; }

    public abstract string Protocol { get; }

    public abstract PortBinding ListenPort { get; set; }

    public bool Enabled { get; set; }

    public HostName? MainDomain { get; set; }
}