using Serilog;
using ShadowVPN2.Components;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Contributors;
using ShadowVPN2.Hubs;
using ShadowVPN2.Infrastructure;
using ShadowVPN2.Infrastructure.Configurations;
using ShadowVPN2.Infrastructure.Middleware;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting web application");
    var builder = WebApplication.CreateBuilder(args);

    // Override the Serilog path to ensure logs are stored in the correct data directory
    // (e.g. /app-data/logs in Docker, or data/logs locally) instead of polluting the project root.
    builder.Configuration["Serilog:WriteTo:1:Args:path"] = (DataUtils.DataFolder / "logs" / "log-.txt").ToString();

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    var localConfiguration = await LocalConfiguration.Initialize();
    builder.Services.AddSingleton(localConfiguration);
    builder.SetupRavenDb(LocalConfiguration.CertificatePfxPath);
    builder.SetupAuthentication();
    builder.SetupIdentity();
    builder.SetupAuthorization();
    builder.SetupContainerValidation();

    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<SetupService>();
    builder.Services.AddScoped<SettingsService>();
    builder.Services.AddSingleton<ProtocolSettingsService>();
    builder.Services.AddSingleton<NodeService>();
    builder.Services.AddSingleton<SingBoxService>();
    builder.Services.AddSingleton<ISingBoxConfigContributor, DefaultOutboundContributor>();
    builder.Services.AddSingleton<ISingBoxConfigContributor, Hysteria2ConfigContributor>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<SingBoxService>());
    builder.Services.AddControllers();
    builder.Services.AddSignalR();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();

    app.UseMiddleware<SetupMiddleware>();

    // Configure Authentication
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapControllers();
    app.MapHub<NodeHub>("/api/node/hub");
    app.MapHub<ClientHub>("/api/client/hub");
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();
    app.MapAdditionalIdentityEndpoints();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}