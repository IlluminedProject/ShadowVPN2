using ShadowVPN2.Entities;

namespace ShadowVPN2.Data;

public static class FreeTurnObfuscationProfileExtensions {
    public static string ToCommandLineValue(this FreeTurnObfuscationProfile profile) {
        return profile switch {
            FreeTurnObfuscationProfile.RtpOpus => "rtpopus",
            FreeTurnObfuscationProfile.RtpOpus2 => "rtpopus2",
            FreeTurnObfuscationProfile.RtpOpus3 => "rtpopus3",
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };
    }
}