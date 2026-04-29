using System.Net;

namespace ShadowVPN2.Data;

public class ClientResponse
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string AssignedIp { get; set; }
    public required bool IsEnabled { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public WireGuardClientSettingsResponse? WireGuard { get; set; }
}

public class WireGuardClientSettingsResponse
{
    public int? Mtu { get; set; }
}
