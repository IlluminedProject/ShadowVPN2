using System.Security.Cryptography;

namespace ShadowVPN2.Entities;

public sealed class FreeTurnTransportSettings : TransportSettings {
    public FreeTurnObfuscationProfile? ObfuscationProfile { get; set; } = FreeTurnObfuscationProfile.RtpOpus3;

    public string? ObfuscationKey { get; set; } = GenerateObfuscationKey();

    public FreeTurnTurnTransport TurnTransport { get; set; } = FreeTurnTurnTransport.Tcp;

    public int Streams { get; set; } = 12;

    public static string GenerateObfuscationKey() {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }
}