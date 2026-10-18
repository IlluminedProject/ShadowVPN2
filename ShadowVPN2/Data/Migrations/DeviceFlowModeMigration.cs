using Newtonsoft.Json.Linq;
using Raven.Client.Documents.Session;
using ShadowVPN2.Contracts.Auth;
using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Migrations;

public sealed class DeviceFlowModeMigration : IRavenMigration {
    public int Version => 1;
    public string Description => "Replace OIDC device flow flag with mode";

    public async Task ApplyAsync(IAsyncDocumentSession session, CancellationToken cancellationToken) {
        var configuration = await session.LoadAsync<JObject>(EntityGlobalConfiguration.ConfigurationDocumentId,
            cancellationToken);
        if (configuration?["Providers"] is not JArray providers)
            return;

        foreach (var provider in providers.OfType<JObject>()) {
            if (!provider.TryGetValue("DeviceFlowEnabled", out var enabled))
                continue;

            provider["DeviceFlowMode"] = (enabled.Value<bool>() ? DeviceFlowMode.Always : DeviceFlowMode.Never).ToString();
            provider.Remove("DeviceFlowEnabled");
        }
    }
}
