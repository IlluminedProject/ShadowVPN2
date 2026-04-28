using System.Text.Json;
using TruePath;

namespace ShadowVPN2.Infrastructure;

public static class DataUtils
{
    public static AbsolutePath DataFolder { get; } = GetBaseDataFolder();

    private static AbsolutePath GetBaseDataFolder()
    {
        var envDir = Environment.GetEnvironmentVariable("SHADOWVPN_DATA_DIR");
        if (!string.IsNullOrEmpty(envDir))
        {
            return new LocalPath(envDir).ResolveToCurrentDirectory();
        }

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            return new AbsolutePath("/data");
        }

        return new LocalPath("data/").ResolveToCurrentDirectory();
    }

    public static JsonSerializerOptions DefaultSerializerOptions { get; } = new()
        { WriteIndented = true, PropertyNameCaseInsensitive = true };
}