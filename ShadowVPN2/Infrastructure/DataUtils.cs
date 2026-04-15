using System.Text.Json;
using TruePath;

namespace ShadowVPN2.Infrastructure;

public static class DataUtils
{
    public static AbsolutePath DataFolder { get; } = new AbsolutePath("/data/");

    public static JsonSerializerOptions DefaultSerializerOptions { get; } = new()
        { WriteIndented = true, PropertyNameCaseInsensitive = true };
}