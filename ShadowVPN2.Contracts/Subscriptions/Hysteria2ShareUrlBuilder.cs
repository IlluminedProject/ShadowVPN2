namespace ShadowVPN2.Contracts.Subscriptions;

public static class Hysteria2ShareUrlBuilder {
    public static string Build(Hysteria2ConnectionInfo connection, string clientName) {
        return new ProxyUriBuilder("hysteria2", connection.ServerAddress, connection.PortBinding.ListenPort)
            .WithCredentials(connection.Password)
            .AddQuery("insecure", "1")
            .AddQuery("pinSHA256", connection.PinSha256)
            .AddQueryIf(connection.ObfsType is not null and not "none", "obfs", connection.ObfsType)
            .AddQueryIf(connection.ObfsType is not null and not "none", "obfs-password", connection.ObfsPassword)
            .AddQuery("sni", connection.Sni?.ToString())
            .AddQuery("mport", connection.PortBinding.PortRangeEnd.HasValue ? connection.PortBinding.ToString() : null)
            .AddQuery("name", clientName)
            .Build();
    }
}