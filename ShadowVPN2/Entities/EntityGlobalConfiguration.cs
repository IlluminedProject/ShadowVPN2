using System.Collections.Generic;
using ShadowVPN2.Entities.Auth;

namespace ShadowVPN2.Entities;

public class EntityGlobalConfiguration
{
    public string Id { get; set; } = "GlobalConfiguration";
    public List<AuthProvider> Providers { get; set; } = new();
}
