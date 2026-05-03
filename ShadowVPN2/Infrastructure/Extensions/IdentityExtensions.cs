using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace ShadowVPN2.Infrastructure.Extensions;

public static class IdentityExtensions
{
    public static async Task<TUser> GetRequiredUserAsync<TUser>(this UserManager<TUser> userManager,
        ClaimsPrincipal principal) where TUser : class
    {
        return await userManager.GetUserAsync(principal)
               ?? throw new UnauthorizedAccessException("User not found");
    }
}