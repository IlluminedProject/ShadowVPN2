using ShadowVPN2.Entities;

namespace ShadowVPN2.Data;

public static class ProtocolSocketKindExtensions {
    public static string ToFreeTurnMode(this ProtocolSocketKind socketKind) {
        return socketKind switch {
            ProtocolSocketKind.Udp => "udp",
            ProtocolSocketKind.Tcp => "tcp",
            _ => throw new ArgumentOutOfRangeException(nameof(socketKind), socketKind, null)
        };
    }
}