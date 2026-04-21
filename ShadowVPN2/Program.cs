using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Identity;
using ShadowVPN2.Components;
using ShadowVPN2.Components.Account;
using ShadowVPN2.Data;
using ShadowVPN2.Infrastructure.Configurations;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var localConfiguration = await LocalConfiguration.Initialize();
builder.Services.AddSingleton(localConfiguration);

// Initialize RavenDB
builder.Services.AddSingleton(RavenDbInitializer.Initialize(LocalConfiguration.CertificatePfxPath.ToString()));
builder.Services.AddScoped<IAsyncDocumentSession>(sp => sp.GetRequiredService<IDocumentStore>().OpenAsyncSession());

// Configure Identity with RavenDB
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
    })
    .AddRoles<Raven.Identity.IdentityRole>()
    .AddRavenDbIdentityStores<ApplicationUser, Raven.Identity.IdentityRole>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Configure Authentication
app.UseAuthentication();
app.UseAuthorization();

await app.RunAsync();