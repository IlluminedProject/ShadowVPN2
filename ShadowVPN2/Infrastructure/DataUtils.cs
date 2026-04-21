using System.Text.Json;
using TruePath;

namespace ShadowVPN2.Infrastructure;

public static class DataUtils
{
    public static AbsolutePath DataFolder { get; } = new LocalPath("data/").ResolveToCurrentDirectory();

    public static JsonSerializerOptions DefaultSerializerOptions { get; } = new()
        { WriteIndented = true, PropertyNameCaseInsensitive = true };
}