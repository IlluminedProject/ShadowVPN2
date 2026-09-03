using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Protocols;

public class UpdateProtocolsSettingsRequest {
    public string? MainDomain { get; set; }
    public List<ProtocolGlobalSettings> Protocols { get; set; } = new();
}