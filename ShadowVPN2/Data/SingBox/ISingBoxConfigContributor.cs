using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.SingBox;

public interface ISingBoxConfigContributor
{
    Task ContributeAsync(SingBoxConfig config, IReadOnlyList<ProtocolGlobalSettings> protocols,
        IReadOnlyList<EntityClient> clients);
}