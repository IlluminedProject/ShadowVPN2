namespace ShadowVPN2.Entities.Proxy;

public class WireGuardClientSettings {
    public int? Mtu { get; set; }
    public string? PrivateKey { get; set; }
    public string? PublicKey { get; set; }
}