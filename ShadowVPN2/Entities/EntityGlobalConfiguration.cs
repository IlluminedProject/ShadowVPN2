using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Entities.Base;

namespace ShadowVPN2.Entities;

public class EntityGlobalConfiguration : IEntityId
{
    public bool SelfRegistrationEnabled { get; set; } = true;
    public List<AuthProvider> Providers { get; set; } = new();
    public List<ProtocolGlobalSettings> Protocols { get; set; } = new();
    public string Id { get; init; } = "GlobalConfiguration";
}