namespace ShadowVPN2.Contracts.Subscriptions;

public static class WireGuardShareUrlBuilder {
    public static string Build(WireGuardConnectionInfo connection) {
        var builder = new ProxyUriBuilder("wg", connection.ServerAddress, connection.ServerPort)
            .WithoutPathSeparator()
            .AddQuery("private_key", connection.PrivateKey)
            .AddQuery("local_address",
                connection.AssignedIp.Contains('/') ? connection.AssignedIp : connection.AssignedIp + "/32")
            .AddQuery("mtu", connection.Mtu.ToString())
            .AddQuery("enable_amnezia", connection.IsAmneziaWg ? "true" : "false")
            .AddQuery("public_key", connection.ServerPublicKey);

        if (connection.IsAmneziaWg) {
            builder.AddQuery("jc", connection.Jc.ToString())
                .AddQuery("jmin", connection.Jmin.ToString())
                .AddQuery("jmax", connection.Jmax.ToString())
                .AddQuery("s1", connection.S1.ToString())
                .AddQuery("s2", connection.S2.ToString())
                .AddQuery("s3", connection.S3.ToString())
                .AddQuery("s4", connection.S4.ToString())
                .AddQuery("h1", connection.H1)
                .AddQuery("h2", connection.H2)
                .AddQuery("h3", connection.H3)
                .AddQuery("h4", connection.H4);
        }

        return builder.AddQuery("persistent_keepalive_interval", "25").Build();
    }
}