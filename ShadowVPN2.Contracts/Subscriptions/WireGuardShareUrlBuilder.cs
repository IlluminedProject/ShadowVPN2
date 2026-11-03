using System.Text;

namespace ShadowVPN2.Contracts.Subscriptions;

public static class WireGuardShareUrlBuilder {
    public static string Build(WireGuardConnectionInfo connection) {
        var builder = new StringBuilder("wg://")
            .Append(connection.ServerAddress.ToAddress(connection.ServerPort).FormatWithPort())
            .Append("?private_key=").Append(Uri.EscapeDataString(connection.PrivateKey))
            .Append("&local_address=").Append(Uri.EscapeDataString(connection.AssignedIp.Contains('/')
                ? connection.AssignedIp
                : connection.AssignedIp + "/32"))
            .Append("&mtu=").Append(connection.Mtu)
            .Append("&enable_amnezia=").Append(connection.IsAmneziaWg ? "true" : "false")
            .Append("&public_key=").Append(Uri.EscapeDataString(connection.ServerPublicKey));

        if (connection.IsAmneziaWg) {
            builder.Append("&jc=").Append(connection.Jc)
                .Append("&jmin=").Append(connection.Jmin)
                .Append("&jmax=").Append(connection.Jmax)
                .Append("&s1=").Append(connection.S1)
                .Append("&s2=").Append(connection.S2)
                .Append("&s3=").Append(connection.S3)
                .Append("&s4=").Append(connection.S4)
                .Append("&h1=").Append(connection.H1)
                .Append("&h2=").Append(connection.H2)
                .Append("&h3=").Append(connection.H3)
                .Append("&h4=").Append(connection.H4);
        }


        builder.Append("&persistent_keepalive_interval=25");

        return builder.ToString();
    }
}