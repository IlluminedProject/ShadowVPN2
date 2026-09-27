using System.ComponentModel.DataAnnotations;

namespace ShadowVPN2.Contracts.Clients;

public sealed class CreateClientRequest {
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; set; }

    public WireGuardClientSettingsRequest? WireGuard { get; set; }
}