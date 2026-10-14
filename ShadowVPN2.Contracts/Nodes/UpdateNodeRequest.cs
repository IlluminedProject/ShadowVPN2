using System.ComponentModel.DataAnnotations;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Nodes;

public sealed class UpdateNodeRequest {
    [Required] public string Name { get; set; } = string.Empty;

    public HostName? Domain { get; set; }
}