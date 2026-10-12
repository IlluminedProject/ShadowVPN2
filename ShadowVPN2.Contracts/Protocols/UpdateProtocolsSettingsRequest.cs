using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Protocols;

public sealed class UpdateProtocolsSettingsRequest {
    public HostName? MainDomain { get; set; }
    public List<ProtocolSettingsDto> Protocols { get; set; } = [];
    public List<TransportSettingsDto> Transports { get; set; } = [];
}