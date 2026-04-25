using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Identity;
using ShadowVPN2.Components.Account;
using ShadowVPN2.Data;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;
using TruePath;

namespace ShadowVPN2.Infrastructure;

public static class ServiceExtensions
{
    public static void SetupRavenDb(this WebApplicationBuilder builder, AbsolutePath certificatePath)
    {
        builder.Services.AddSingleton(RavenDbInitializer.Initialize(certificatePath.ToString()));
        builder.Services.AddScoped<IAsyncDocumentSession>(sp => sp.GetRequiredService<IDocumentStore>().OpenAsyncSession());
    }
    
    public static void SetupAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        
        builder.Services.AddSingleton<DynamicAuthenticationManager>();
        builder.Services.AddHostedService<DynamicAuthInitializerService>();

        var authBuilder = builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });

        authBuilder.AddIdentityCookies();
    }

    public static void SetupIdentity(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<Raven.Identity.IdentityRole>()
            .AddRavenDbIdentityStores<ApplicationUser, Raven.Identity.IdentityRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services
            .AddScoped<IUserStore<ApplicationUser>, AdvancedUserStore<ApplicationUser, Raven.Identity.IdentityRole>>();
    }
}