using Raven.Client.Documents.Session;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.Migrations;

public sealed class InitializeNaiveProxyClientSettingsMigration : IRavenMigration {
    public int Version => 3;
    public string Description => "Initialize NaiveProxy credentials for existing clients";

    public async Task ApplyAsync(IAsyncDocumentSession session, CancellationToken cancellationToken) {
        var clients = await session.Advanced.LoadStartingWithAsync<EntityClient>(
            "Clients/", null, 0, int.MaxValue, null, null, cancellationToken);

        foreach (var client in clients) {
            client.NaiveProxy ??= NaiveProxyClientSettings.Create();
        }
    }
}
