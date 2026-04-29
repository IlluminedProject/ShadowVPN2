using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data;

public static class ClientMapper
{
    public static ClientResponse MapToResponse(EntityClient client) => new()
    {
        Id = client.Id,
        Name = client.Name,
        AssignedIp = client.GetAssignedIp().ToString(),
        IsEnabled = client.IsEnabled,
        CreatedAt = client.CreatedAt,
        WireGuard = client.WireGuard is not null
            ? new WireGuardClientSettingsResponse { Mtu = client.WireGuard.Mtu }
            : null
    };
}
