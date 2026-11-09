using Microsoft.Extensions.DependencyInjection;
using ShadowVPN2.Infrastructure.Containers.Docker;
using ShadowVPN2.Infrastructure.Containers.Registry;

namespace ShadowVPN2.Infrastructure.Containers;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddContainerRegistry(this IServiceCollection services) {
        services.AddHttpClient();
        services.AddSingleton<RegistryImageClient>();
        return services;
    }

    public static IServiceCollection AddDockerEngine(this IServiceCollection services) {
        services.AddSingleton<DockerApiClient>();
        return services;
    }
}