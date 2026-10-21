using ShadowVPN2.Updater;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<UpdaterOptions>(builder.Configuration.GetSection("Updater"));
builder.Services.AddSingleton<DockerApiClient>();
builder.Services.AddSingleton<ContainerUpdater>();
builder.Services.AddHostedService<UpdaterSocketService>();
await builder.Build().RunAsync();