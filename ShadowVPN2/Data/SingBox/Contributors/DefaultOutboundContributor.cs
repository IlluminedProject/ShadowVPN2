using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.SingBox.Contributors;

public class DefaultOutboundContributor : ISingBoxConfigContributor
{
    public Task ContributeAsync(SingBoxConfig config, IReadOnlyList<ProtocolGlobalSettings> protocols,
        IReadOnlyList<EntityClient> clients)
    {
        config.Outbounds.Add(new DirectOutboundConfig
        {
            Tag = "direct"
        });

        return Task.CompletedTask;
    }
}