using System.Text.Json.Serialization;

namespace ShadowVPN2.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FreeTurnObfuscationProfile {
    RtpOpus,
    RtpOpus2,
    RtpOpus3
}