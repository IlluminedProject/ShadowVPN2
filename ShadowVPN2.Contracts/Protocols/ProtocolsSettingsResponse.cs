namespace ShadowVPN2.Contracts.Protocols;

public sealed class ProtocolsSettingsResponse {
    public string? MainDomain { get; set; }
    public List<ProtocolSettingsDto> Protocols { get; set; } = [];
    public List<TransportSettingsDto> Transports { get; set; } = [];
}