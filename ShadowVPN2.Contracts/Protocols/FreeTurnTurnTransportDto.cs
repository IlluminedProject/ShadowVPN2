using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Protocols;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FreeTurnTurnTransportDto {
    Tcp,
    Udp
}