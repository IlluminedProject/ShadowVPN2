using Newtonsoft.Json.Linq;
using Raven.Client.Documents.Session;
using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Migrations;

public sealed class UnifyAwgSettingsMigration : IRavenMigration {
    private static readonly string[] HeaderParameterNames = ["H1", "H2", "H3", "H4"];

    public int Version => 1;
    public string Description => "Unify cluster and protocol AmneziaWG settings";

    public async Task ApplyAsync(IAsyncDocumentSession session, CancellationToken cancellationToken) {
        var configuration = await session.LoadAsync<JObject>(
            EntityGlobalConfiguration.ConfigurationDocumentId, cancellationToken);
        if (configuration is null)
            return;

        if (configuration["AwgSettings"] is JObject awgSettings)
            NormalizeSettings(awgSettings);

        if (configuration["Protocols"] is JArray protocols) {
            foreach (var protocol in protocols.OfType<JObject>()) {
                if (string.Equals(protocol["Protocol"]?.Value<string>(), "WireGuard",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(protocol["$type"]?.Value<string>(), "wireguard", StringComparison.OrdinalIgnoreCase))
                    NormalizeSettings(protocol);
            }
        }

        await session.StoreAsync(configuration, EntityGlobalConfiguration.ConfigurationDocumentId, cancellationToken);
    }

    private static void NormalizeSettings(JObject settings) {
        if (settings["AmneziaWg"] is JObject parameters) {
            foreach (var property in parameters.Properties())
                settings[property.Name] = property.Value.DeepClone();

            settings.Remove("AmneziaWg");
        }

        foreach (var propertyName in HeaderParameterNames) {
            if (settings[propertyName] is { Type: JTokenType.Integer } value)
                settings[propertyName] = value.ToString();
        }
    }
}
