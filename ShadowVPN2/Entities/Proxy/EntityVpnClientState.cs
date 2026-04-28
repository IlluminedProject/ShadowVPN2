namespace ShadowVPN2.Entities.Proxy;

public class EntityVpnClientState
{
    // Id mirrors the client: VpnClientStates/{userNumber}/{clientNumber}
    public string Id { get; set; } = null!;

    // Reference to EntityVpnClient document
    public string ClientId { get; set; } = null!;

    public bool IsOnline { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    // The node the client is currently connected to
    public string? ConnectedNodeId { get; set; }
}
