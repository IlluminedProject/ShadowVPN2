using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Identity;
using ShadowVPN2.Components.Account;
using ShadowVPN2.Data;
using ShadowVPN2.Infrastructure.Authentication;
using TruePath;
using IdentityRole = Raven.Identity.IdentityRole;

namespace ShadowVPN2.Infrastructure;

public static class ServiceExtensions
{
    public static void SetupRavenDb(this WebApplicationBuilder builder, AbsolutePath certificatePath)
    {
        builder.Services.AddSingleton(RavenDbInitializer.Initialize(certificatePath.ToString()));
        builder.Services.AddScoped<IAsyncDocumentSession>(sp =>
            sp.GetRequiredService<IDocumentStore>().OpenAsyncSession());
        builder.Services.AddSingleton<ClientService>();
    }

    public static void SetupAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo((DataUtils.DataFolder / "keys").ToString()))
            .SetApplicationName("ShadowVPN2");
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
        builder.Services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
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
            .AddRoles<IdentityRole>()
            .AddRavenDbIdentityStores<ApplicationUser, IdentityRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.Configure<RavenDbIdentityOptions>(options => options.AutoSaveChanges = true);

        builder.Services
            .AddScoped<IUserStore<ApplicationUser>, AdvancedUserStore<ApplicationUser, IdentityRole>>();
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

    public static void SetupContainerValidation(this WebApplicationBuilder builder)
    {
        builder.Host.UseDefaultServiceProvider((context, options) =>
        {
            options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
            options.ValidateOnBuild = context.HostingEnvironment.IsDevelopment();
        });
    }
}