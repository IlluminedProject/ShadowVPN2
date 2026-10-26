using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Infrastructure.Configurations;
using ShadowVPN2.Infrastructure.RavenDb.Migrations;
using TruePath.SystemIo;

namespace ShadowVPN2.Infrastructure;

public sealed class ApplicationBootstrapper(
    IConfiguration configuration,
    IOptions<LocalConfiguration> localConfigurationOptions,
    MeshService meshService,
    IServiceProvider services,
    ILogger<ApplicationBootstrapper> logger) {
    public async Task InitializeAsync() {
        LocalConfiguration.Path.CreateDirectory();
        var joinToken = configuration["JoinToken"];
        var firstLaunch = FirstLaunchExperienceHelpers.IsFirstLaunch();
        if (firstLaunch && !string.IsNullOrEmpty(joinToken)) {
            var join = await FirstLaunchExperienceHelpers.InitializeFromJoinToken(joinToken, meshService, services);

            logger.LogInformation("Completing cluster join");
            await FirstLaunchExperienceHelpers.CompleteJoinAsync(join.Token, join.Response, join.ConnectedTo,
                services.GetRequiredService<IDocumentStore>(), services);
            await RunMigrationsAsync();
            return;
        }

        if (firstLaunch)
            await FirstLaunchExperienceHelpers.InitializeFirstNode();

        localConfigurationOptions.Value.CopyFrom(await LocalConfiguration.LoadAsync());
        await PeerCertificateTrustService.RestoreRootStoreAsync();
        await meshService.InitializeFromDatabaseAsync(CancellationToken.None);
        await RunMigrationsAsync();
    }

    private async Task RunMigrationsAsync() {
        var runner = services.GetRequiredService<RavenMigrationRunner>();
        var cancellationToken = services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        if (!await runner.HasPendingMigrationsAsync(cancellationToken))
            return;

        await runner.RunAsync(cancellationToken);
    }
}