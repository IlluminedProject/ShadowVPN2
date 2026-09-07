using Microsoft.Extensions.Options;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Contributors;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Infrastructure;

public static class ApplicationServices {
    public static void AddApplicationServices(this WebApplicationBuilder builder) {
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddOptions<SingBoxOptions>().BindConfiguration("SingBox");
        builder.Services.AddSingleton<IPostConfigureOptions<SingBoxOptions>, SingBoxOptionsPostConfigure>();
        builder.Services.AddSingleton<AwgTunCapabilityProbe>();
        builder.Services.AddOptions<LocalConfiguration>();
        builder.Services.AddSingleton<ApplicationBootstrapper>();
        builder.Services.AddSingleton<ManagedCertificateService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ManagedCertificateService>());
        builder.Services.AddSingleton<Http01ChallengeServer>();
        builder.Services.AddSingleton<CertificateSettingsService>();
        builder.Services.AddSingleton<AcmeCertificateService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<AcmeCertificateService>());

        builder.SetupKestrelHttps();
        builder.SetupRavenDb();
        builder.SetupAuthentication();
        builder.SetupIdentity();
        builder.SetupAuthorization();
        builder.SetupContainerValidation();

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<GlobalConfigurationService>();
        builder.Services.AddSingleton<WireGuardKeyService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GlobalConfigurationService>());
        builder.Services.AddSingleton<SetupService>();
        builder.Services.AddScoped<SettingsService>();
        builder.Services.AddScoped<SubscriptionService>();
        builder.Services.AddSingleton<ISubscriptionConnectionContributor, Hysteria2SubscriptionContributor>();
        builder.Services.AddSingleton<ISubscriptionConnectionContributor, WireGuardSubscriptionContributor>();
        builder.Services.AddSingleton<ProtocolSettingsService>();
        builder.Services.AddSingleton<ProtocolDomainService>();
        builder.Services.AddSingleton<NodeService>();
        builder.Services.AddSingleton<NodeNetworkService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<NodeNetworkService>());
        builder.Services.AddSingleton<DomainValidationService>();
        builder.Services.AddSingleton<SingBoxProcessManager>();
        builder.Services.AddSingleton<SingBoxService>();
        builder.Services.AddSingleton<ISingBoxConfigContributor, DefaultOutboundContributor>();
        builder.Services.AddSingleton<ISingBoxConfigContributor, Hysteria2ConfigContributor>();
        builder.Services.AddSingleton<ISingBoxConfigContributor, AwgMeshConfigContributor>();
        builder.Services.AddSingleton<ISingBoxConfigContributor, WireGuardAmneziaConfigContributor>();
        builder.Services.AddSingleton<ClusterService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SingBoxService>());
        builder.Services.AddControllers();
        builder.Services.AddSignalR();
    }
}
