using Raven.Client.Documents.Session;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.RavenDb.Migrations;

namespace ShadowVPN2.Data.Migrations;

public sealed class AwgHeaderNullabilityMigration : IRavenMigration {
    public int Version {
        get => 3;
    }

    public string Description {
        get => "Normalize default AmneziaWG headers to null";
    }

    public async Task ApplyAsync(IAsyncDocumentSession session, CancellationToken cancellationToken) {
        var configuration = await session.LoadAsync<EntityGlobalConfiguration>(
            EntityGlobalConfiguration.ConfigurationDocumentId, cancellationToken);
        if (configuration is null)
            return;

        Normalize(configuration.AwgSettings);
        foreach (var settings in configuration.Protocols.OfType<AwgGlobalSettings>())
            Normalize(settings);

        await session.SaveChangesAsync(cancellationToken);
    }

    private static void Normalize(IAwgSettings settings) {
        settings.H1 = Normalize(settings.H1, "1");
        settings.H2 = Normalize(settings.H2, "2");
        settings.H3 = Normalize(settings.H3, "3");
        settings.H4 = Normalize(settings.H4, "4");
    }

    private static string? Normalize(string? value, string defaultValue) {
        return value is null || value == defaultValue || string.IsNullOrWhiteSpace(value) ? null : value;
    }
}