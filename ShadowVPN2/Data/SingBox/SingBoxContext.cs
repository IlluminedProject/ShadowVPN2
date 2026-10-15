using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data.SingBox;

public sealed record SingBoxContext(
    SingBoxConfig Config,
    IReadOnlyList<EntityClient> Clients,
    IReadOnlyList<ProtocolGlobalSettings> Protocols,
    IReadOnlyList<TransportSettings> Transports);