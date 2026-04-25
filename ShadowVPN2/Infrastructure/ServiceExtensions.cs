using Microsoft.AspNetCore.Identity;
using Raven.Identity;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Infrastructure;

public static class ServiceExtensions
{
    public static WebApplicationBuilder SetupIdentity(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<Raven.Identity.IdentityRole>()
            .AddRavenDbIdentityStores<ApplicationUser, Raven.Identity.IdentityRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services
            .AddScoped<IUserStore<ApplicationUser>, AdvancedUserStore<ApplicationUser, Raven.Identity.IdentityRole>>();
        return builder;
    }
}