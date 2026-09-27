using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Protocols;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FreeTurnObfuscationProfileDto {
    RtpOpus,
    RtpOpus2,
    RtpOpus3
}