using Newtonsoft.Json.Linq;
using Raven.Client.Documents.Session;
using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Migrations;

public sealed class RenameWireGuardAmneziaSettingsMigration : IRavenMigration {
    private const string LegacyType = "ShadowVPN2.Entities.WireGuardAmneziaGlobalSettings, ShadowVPN2";
    private const string CurrentType = "ShadowVPN2.Entities.AwgGlobalSettings, ShadowVPN2";

    public int Version => 2;
    public string Description => "Rename persisted WireGuard Amnezia settings type";

    public async Task ApplyAsync(IAsyncDocumentSession session, CancellationToken cancellationToken) {
        var configuration = await session.LoadAsync<JObject>(
            EntityGlobalConfiguration.ConfigurationDocumentId, cancellationToken);
        if (configuration?["Protocols"] is not JArray protocols)
            return;

        var changed = false;
        foreach (var protocol in protocols.OfType<JObject>()) {
            if (!string.Equals(protocol["$type"]?.Value<string>(), LegacyType, StringComparison.Ordinal))
                continue;

            protocol["$type"] = CurrentType;
            changed = true;
        }

        if (changed) {
            await session.StoreAsync(configuration, EntityGlobalConfiguration.ConfigurationDocumentId,
                cancellationToken);
        }
    }
}
