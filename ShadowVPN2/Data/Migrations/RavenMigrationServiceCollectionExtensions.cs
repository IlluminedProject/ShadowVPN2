namespace ShadowVPN2.Data.Migrations;

public static class RavenMigrationServiceCollectionExtensions {
    public static IServiceCollection AddRavenMigration<TMigration>(this IServiceCollection services)
        where TMigration : class, IRavenMigration {
        services.AddSingleton<IRavenMigration, TMigration>();
        return services;
    }
}