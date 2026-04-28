using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
        builder.Services.AddScoped<VpnClientService>();
    }

    public static void SetupAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddDataProtection();
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

        // Register OIDC infrastructure without a real scheme to avoid NullReferenceException in handlers.
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
    }

    public static void SetupIdentity(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedPhoneNumber = false;
            })
            .AddRoles<Raven.Identity.IdentityRole>()
            .AddRavenDbIdentityStores<ApplicationUser, Raven.Identity.IdentityRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.Configure<RavenDbIdentityOptions>(options => options.AutoSaveChanges = true);

        builder.Services
            .AddScoped<IUserStore<ApplicationUser>, AdvancedUserStore<ApplicationUser, Raven.Identity.IdentityRole>>();
    }

    public static void SetupAuthorization(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthorization(options =>
        {
            foreach (var permission in AppPermissions.All)
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireClaim(AppPermissions.PermissionClaimType, permission));
            }
        });

        builder.Services.AddHostedService<RolePermissionInitializer>();
    }
}