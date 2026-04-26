namespace ShadowVPN2.Infrastructure.Authentication;

public static class AppRoles
{
    public const string Administrator = "Administrator";
    public const string User = "User";

    /// <summary>
    /// Defines the default permissions for each role.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRolePermissions =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Administrator] = AppPermissions.All,

            [User] =
            [
                AppPermissions.Nodes.View,
                AppPermissions.Settings.View
            ]
        };
}