using System.ComponentModel.DataAnnotations;

namespace ShadowVPN2.Data;

public class CreateClientRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; set; }

    public WireGuardClientSettingsRequest? WireGuard { get; set; }
}

public class UpdateClientRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    public WireGuardClientSettingsRequest? WireGuard { get; set; }
}

public class WireGuardClientSettingsRequest
{
    [Range(576, 9000)]
    public int? Mtu { get; set; }
}
