using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Data.Subscription.Models.Connections;

public sealed class WireGuardConnectionInfo : ProtocolConnectionInfo {
    public required HostName ServerAddress { get; set; }
    public required int ServerPort { get; set; }
    public required string PrivateKey { get; set; }
    public required string AssignedIp { get; set; }
    public required string ServerPublicKey { get; set; }
    public required int Mtu { get; set; }
    public required bool IsAmneziaWg { get; set; }
    public uint? Jc { get; set; }
    public uint? Jmin { get; set; }
    public uint? Jmax { get; set; }
    public int? S1 { get; set; }
    public int? S2 { get; set; }
    public int? S3 { get; set; }
    public int? S4 { get; set; }
    public string? H1 { get; set; }
    public string? H2 { get; set; }
    public string? H3 { get; set; }
    public string? H4 { get; set; }
    public string? I1 { get; set; }
    public string? I2 { get; set; }
    public string? I3 { get; set; }
    public string? I4 { get; set; }
    public string? I5 { get; set; }

    public string CreateShareUrl() {
        return WireGuardShareUrlBuilder.Build(ToContract());
    }

    private Contracts.Subscriptions.WireGuardConnectionInfo ToContract() {
        return new Contracts.Subscriptions.WireGuardConnectionInfo {
            ServerAddress = ServerAddress, ServerPort = ServerPort, PrivateKey = PrivateKey,
            AssignedIp = AssignedIp, ServerPublicKey = ServerPublicKey, Mtu = Mtu, IsAmneziaWg = IsAmneziaWg,
            Jc = Jc, Jmin = Jmin, Jmax = Jmax, S1 = S1, S2 = S2, S3 = S3, S4 = S4,
            H1 = H1, H2 = H2, H3 = H3, H4 = H4, I1 = I1, I2 = I2, I3 = I3, I4 = I4, I5 = I5
        };
    }

    public WireGuardConnectionInfo WithEndpoint(HostName serverAddress, int serverPort, int mtu) {
        return new WireGuardConnectionInfo {
            ServerAddress = serverAddress,
            ServerPort = serverPort,
            PrivateKey = PrivateKey,
            AssignedIp = AssignedIp,
            ServerPublicKey = ServerPublicKey,
            Mtu = mtu,
            IsAmneziaWg = IsAmneziaWg,
            Jc = Jc,
            Jmin = Jmin,
            Jmax = Jmax,
            S1 = S1,
            S2 = S2,
            S3 = S3,
            S4 = S4,
            H1 = H1,
            H2 = H2,
            H3 = H3,
            H4 = H4,
            I1 = I1,
            I2 = I2,
            I3 = I3,
            I4 = I4,
            I5 = I5
        };
    }
}