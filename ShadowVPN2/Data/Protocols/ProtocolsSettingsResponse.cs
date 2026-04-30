using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Protocols;

public class ProtocolsSettingsResponse
{
    public List<ProtocolGlobalSettings> Protocols { get; set; } = new();
}