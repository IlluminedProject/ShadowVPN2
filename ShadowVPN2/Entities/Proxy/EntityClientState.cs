namespace ShadowVPN2.Entities.Proxy;

public class EntityClientState
{
    // Id mirrors the client: ClientStates/{userNumber}/{clientNumber}
    public string Id { get; set; } = null!;

    // Reference to EntityClient document
    public string ClientId { get; set; } = null!;

    public bool IsOnline { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    // The node the client is currently connected to
    public string? ConnectedNodeId { get; set; }
}
