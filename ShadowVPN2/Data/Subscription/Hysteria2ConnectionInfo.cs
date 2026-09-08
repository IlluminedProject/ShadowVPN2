namespace ShadowVPN2.Data.Subscription;

public class Hysteria2ConnectionInfo : ProtocolConnectionInfo {
    public required string ServerAddress { get; set; }
    public int ServerPort { get; set; }
    public required string Password { get; set; }
    public string? ObfsType { get; set; }
    public string? ObfsPassword { get; set; }
    public string? Sni { get; set; }
    public string? PinSha256 { get; set; }

    public string CreateShareUrl(string clientName) {
        var builder = new ProxyUriBuilder("hysteria2", ServerAddress, ServerPort)
            .WithCredentials(Password)
            .AddQuery("insecure", "1")
            .AddQuery("pinSHA256", PinSha256)
            .AddQueryIf(ObfsType is not null and not "none", "obfs", ObfsType)
            .AddQueryIf(ObfsType is not null and not "none", "obfs-password", ObfsPassword)
            .AddQuery("sni", Sni)
            .AddQuery("name", clientName);

        return builder.Build();
    }
}