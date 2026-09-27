namespace ShadowVPN2.Contracts.Subscriptions;

public sealed class FreeTurnConnectionInfo : TransportConnectionInfo {
    public required string Peer { get; set; }
    public required string? ObfuscationProfile { get; set; }
    public required string? ObfuscationKey { get; set; }
    public required string TurnTransport { get; set; }
    public required int Mode { get; set; }
    public required int Streams { get; set; }
    public required string ClientId { get; set; }
}