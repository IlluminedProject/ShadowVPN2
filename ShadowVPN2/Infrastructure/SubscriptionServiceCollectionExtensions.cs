using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Data.Subscription.Contributors;
using ShadowVPN2.Data.Subscription.Exporters;
using ShadowVPN2.Data.Subscription.Renderers;
using ShadowVPN2.Data.Subscription.Services;
using ShadowVPN2.Frontend;

namespace ShadowVPN2.Infrastructure;

public static class SubscriptionServiceCollectionExtensions {
    public static IServiceCollection AddSubscriptions(this IServiceCollection services) {
        services.AddScoped<ISubscriptionApplicationService, SubscriptionApplicationService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<ISubscriptionSource>(sp => sp.GetRequiredService<SubscriptionService>());
        services.AddSingleton<ISubscriptionProtocolExporter, Hysteria2SubscriptionProtocolExporter>();
        services.AddSingleton<ISubscriptionProtocolExporter, NaiveProxySubscriptionProtocolExporter>();
        services.AddSingleton<ISubscriptionProtocolExporter, WireGuardSubscriptionProtocolExporter>();
        services.AddSingleton<SubscriptionProtocolExporterRegistry>();
        services.AddSingleton<ISubscriptionRenderer, Base64SubscriptionRenderer>();
        services.AddSingleton<ISubscriptionRenderer, MihomoSubscriptionRenderer>();
        services.AddSingleton<ISubscriptionRenderer, MihomoProviderSubscriptionRenderer>();
        services.AddSingleton<ISubscriptionRenderer, FreeTurnSubscriptionRenderer>();
        services.AddSingleton<ISubscriptionRenderer, StashSubscriptionRenderer>();
        services.AddSingleton<ISubscriptionRenderer, ClashSubscriptionRenderer>();
        services.AddSingleton<ISubscriptionRenderer, SingBoxSubscriptionRenderer>();
        services.AddSingleton<ISubscriptionRenderer, XrayJsonSubscriptionRenderer>();
        services.AddSingleton<SubscriptionRendererRegistry>();
        services.AddSingleton<SubscriptionFormatService>();
        services.AddSingleton<ISubscriptionConnectionContributor, Hysteria2SubscriptionContributor>();
        services.AddSingleton<ISubscriptionConnectionContributor, NaiveProxySubscriptionContributor>();
        services.AddSingleton<ISubscriptionConnectionContributor, NaiveProxyQuicSubscriptionContributor>();
        services.AddSingleton<ISubscriptionConnectionContributor, WireGuardSubscriptionContributor>();
        return services;
    }
}
