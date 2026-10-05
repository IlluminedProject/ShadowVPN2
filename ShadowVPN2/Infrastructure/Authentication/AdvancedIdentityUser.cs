using Microsoft.AspNetCore.Identity;
using IdentityUser = Raven.Identity.IdentityUser;

namespace ShadowVPN2.Infrastructure.Authentication;

public class AdvancedIdentityUser : IdentityUser {
    public List<UserPasskeyInfo> Passkeys { get; set; } = new();
}