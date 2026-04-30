namespace ShadowVPN2.Data;

public class ClientResponse
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string AssignedIp { get; set; }
    public required bool IsEnabled { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public WireGuardClientSettingsResponse? WireGuard { get; set; }
    public Hysteria2ClientSettingsResponse? Hysteria2 { get; set; }
}

public class WireGuardClientSettingsResponse
{
    public int? Mtu { get; set; }
}

public class Hysteria2ClientSettingsResponse
{
    public string? Password { get; set; }
}