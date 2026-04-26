namespace ShadowVPN2.Infrastructure.Authentication;

public static class AppPermissions
{
    public const string PermissionClaimType = "Permission";

    public static class Nodes
    {
        public const string View = "Nodes.View";
        public const string Manage = "Nodes.Manage";
    }

    public static class Settings
    {
        public const string View = "Settings.View";
        public const string Manage = "Settings.Manage";
    }

    public static class Users
    {
        public const string View = "Users.View";
        public const string Manage = "Users.Manage";
    }

    /// <summary>
    /// Returns all available permissions for automation.
    /// </summary>
    public static IReadOnlyList<string> All => new[]
    {
        Nodes.View,
        Nodes.Manage,
        Settings.View,
        Settings.Manage,
        Users.View,
        Users.Manage
    };
}
