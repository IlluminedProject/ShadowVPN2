namespace ShadowVPN2.Contracts.Extensions;

public static class StringExtensions {
    public static string? NullIfWhiteSpace(this string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static string? NullIfDefault(this string? value, string defaultValue) {
        return value.NullIfWhiteSpace() is { } normalized && normalized != defaultValue ? normalized : null;
    }
}