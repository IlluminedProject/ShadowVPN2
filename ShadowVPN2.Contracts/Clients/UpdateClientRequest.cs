using System.ComponentModel.DataAnnotations;

namespace ShadowVPN2.Contracts.Clients;

public sealed class UpdateClientRequest {
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    public WireGuardClientSettingsRequest? WireGuard { get; set; }
}