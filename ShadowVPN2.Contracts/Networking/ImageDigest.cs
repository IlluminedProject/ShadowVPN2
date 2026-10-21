using System.Text.RegularExpressions;

namespace ShadowVPN2.Contracts.Networking;

public static partial class ImageDigest {
    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    public static bool IsValid(string? value) {
        return value is not null && Sha256Pattern().IsMatch(value);
    }
}