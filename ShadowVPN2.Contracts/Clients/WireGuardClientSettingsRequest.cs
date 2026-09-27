using System.ComponentModel.DataAnnotations;

namespace ShadowVPN2.Contracts.Clients;

public sealed class WireGuardClientSettingsRequest {
    [Range(576, 9000)] public int? Mtu { get; set; }
}