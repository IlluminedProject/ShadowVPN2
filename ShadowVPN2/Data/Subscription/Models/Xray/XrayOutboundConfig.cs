using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.Subscription.Models.Xray;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "protocol")]
[JsonDerivedType(typeof(XrayHysteriaOutboundConfig), "hysteria")]
[JsonDerivedType(typeof(XrayWireGuardOutboundConfig), "wireguard")]
public abstract class XrayOutboundConfig {
    [JsonPropertyName("tag")] public string Tag { get; set; } = null!;
}
