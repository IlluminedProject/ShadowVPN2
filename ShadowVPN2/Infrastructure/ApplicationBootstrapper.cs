using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Infrastructure.Configurations;
using TruePath.SystemIo;

namespace ShadowVPN2.Infrastructure;

public sealed class ApplicationBootstrapper(
    IConfiguration configuration,
    IOptions<LocalConfiguration> localConfigurationOptions,
    IOptions<SingBoxOptions> singBoxOptions,
    SingBoxProcessManager singBoxProcessManager,
    IServiceProvider services,
    ILogger<ApplicationBootstrapper> logger) {
    public async Task InitializeAsync() {
        LocalConfiguration.Path.CreateDirectory();
        var joinToken = configuration["JoinToken"];
        var firstLaunch = FirstLaunchExperienceHelpers.IsFirstLaunch();

        if (firstLaunch && !string.IsNullOrEmpty(joinToken)) {
            var join = await FirstLaunchExperienceHelpers.InitializeFromJoinToken(joinToken);
            localConfigurationOptions.Value.CopyFrom(await LocalConfiguration.LoadAsync());

            logger.LogInformation("Completing cluster join");
            await FirstLaunchExperienceHelpers.CompleteJoinAsync(join.Token, join.Response, join.AwgPrivateKey,
                singBoxOptions, singBoxProcessManager, services.GetRequiredService<IDocumentStore>());
            return;
        }

        if (firstLaunch)
            await FirstLaunchExperienceHelpers.InitializeFirstNode();

        localConfigurationOptions.Value.CopyFrom(await LocalConfiguration.LoadAsync());
    }
}