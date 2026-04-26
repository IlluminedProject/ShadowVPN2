using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace ShadowVPN2.Infrastructure.Authentication;

public class RolePermissionInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<RolePermissionInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting role and permission synchronization...");

        using var scope = scopeFactory.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Raven.Identity.IdentityRole>>();

        try
        {
            foreach (var (roleName, permissions) in AppRoles.DefaultRolePermissions)
            {
                await SyncRolePermissions(roleManager, roleName, permissions, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while synchronizing roles and permissions");
        }

        logger.LogInformation("Role and permission synchronization completed");
    }

    private async Task SyncRolePermissions(
        RoleManager<Raven.Identity.IdentityRole> roleManager,
        string roleName,
        IEnumerable<string> targetPermissions,
        CancellationToken ct)
    {
        // 1. Ensure role exists
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            logger.LogInformation("Creating {Role} role", roleName);
            await roleManager.CreateAsync(new Raven.Identity.IdentityRole(roleName));
        }

        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null) return;

        // 2. Get existing claims for the role
        var existingClaims = await roleManager.GetClaimsAsync(role);
        var existingPermissionClaims = existingClaims
            .Where(c => c.Type == AppPermissions.PermissionClaimType)
            .Select(c => c.Value)
            .ToHashSet();

        // 3. Add missing permissions
        foreach (var permission in targetPermissions)
        {
            if (!existingPermissionClaims.Contains(permission))
            {
                logger.LogInformation("Adding permission {Permission} to role {Role}", permission, roleName);
                await roleManager.AddClaimAsync(role, new Claim(AppPermissions.PermissionClaimType, permission));
            }
        }

        // 4. Remove stale permissions
        var permissionsInCode = targetPermissions.ToHashSet();
        foreach (var claimValue in existingPermissionClaims)
        {
            if (!permissionsInCode.Contains(claimValue))
            {
                logger.LogInformation("Removing stale permission {Permission} from role {Role}", claimValue, roleName);
                var claimToRemove = existingClaims.First(c =>
                    c.Type == AppPermissions.PermissionClaimType && c.Value == claimValue);
                await roleManager.RemoveClaimAsync(role, claimToRemove);
            }
        }
    }
}
