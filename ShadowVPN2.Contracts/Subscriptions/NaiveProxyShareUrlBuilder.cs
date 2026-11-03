namespace ShadowVPN2.Contracts.Subscriptions;

public static class NaiveProxyShareUrlBuilder {
    public static string Build(NaiveProxyConnectionInfo connection, string clientName) {
        var scheme = connection.Quic ? "naive+quic" : "naive+https";
        return new ProxyUriBuilder(scheme, connection.ServerAddress, connection.ServerPort)
            .WithoutPathSeparator()
            .WithCredentials(connection.Username, connection.Password)
            .AddQuery("sni", connection.Sni.ToString())
            .AddQueryIf(connection.Insecure, "insecure", "1")
            .AddQuery("pinSHA256", connection.PinSha256)
            .AddQuery("insecure-concurrency", "2")
            .AddQuery("uot", "1")
            .AddQuery("udp_over_tcp", "1")
            .AddQuery("name", clientName)
            .Build();
    }
}