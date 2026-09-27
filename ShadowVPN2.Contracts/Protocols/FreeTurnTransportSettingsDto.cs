namespace ShadowVPN2.Contracts.Protocols;

public sealed class FreeTurnTransportSettingsDto : TransportSettingsDto {
    public FreeTurnObfuscationProfileDto? ObfuscationProfile { get; set; } = FreeTurnObfuscationProfileDto.RtpOpus3;
    public string? ObfuscationKey { get; set; }
    public FreeTurnTurnTransportDto TurnTransport { get; set; } = FreeTurnTurnTransportDto.Tcp;
    public int Streams { get; set; } = 12;
}