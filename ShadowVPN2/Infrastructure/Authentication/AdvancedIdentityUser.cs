using Microsoft.AspNetCore.Identity;

namespace ShadowVPN2.Infrastructure.Authentication;

public class AdvancedIdentityUser : Raven.Identity.IdentityUser
{
    public List<UserPasskeyInfo> Passkeys { get; set; } = new();
}