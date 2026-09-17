using System.Text.Json.Serialization;

namespace ShadowVPN2.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FreeTurnTurnTransport {
    Tcp,
    Udp
}