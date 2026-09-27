namespace ShadowVPN2.Contracts.Clients;

public sealed class ClientResponse {
    public required string Id { get; set; }
    public required Guid SubscriptionId { get; set; }
    public required string Name { get; set; }
    public required string AssignedIp { get; set; }
    public required bool IsEnabled { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public WireGuardClientSettingsResponse? WireGuard { get; set; }
    public Hysteria2ClientSettingsResponse? Hysteria2 { get; set; }
}