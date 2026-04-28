using System.Net;

namespace ShadowVPN2.Entities.Proxy;

public class EntityVpnClient
{
    public string Id { get; set; } = null!;

    // Reference to ApplicationUser document
    public string UserId { get; set; } = null!;

    public string Name { get; set; } = null!;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Protocol-specific overrides (null = use cluster defaults)
    public WireGuardClientSettings? WireGuard { get; set; }

    /// <summary>
    /// IP is computed from the Id: VpnClients/{userNumber}/{clientNumber} → 100.64.userNumber.clientNumber
    /// </summary>
    public IPAddress GetAssignedIp()
    {
        var parts = Id.Split('/');
        return IPAddress.Parse($"100.64.{parts[1]}.{parts[2]}");
    }
}
