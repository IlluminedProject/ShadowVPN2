using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Data.Subscription;
using Contract = ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Frontend;

public sealed class SubscriptionApplicationService(SubscriptionService subscriptionService)
    : ISubscriptionApplicationService {
    public async Task<Contract.SubscriptionResponse?> GetSubscriptionAsync(Guid id,
        CancellationToken cancellationToken = default) {
        var response = await subscriptionService.GetSubscriptionAsync(id);
        if (response is null)
            return null;

        return new Contract.SubscriptionResponse {
            ClientName = response.ClientName,
            Protocols = response.Protocols.Select(protocol => new Contract.ProtocolSubscription {
                Protocol = protocol.Protocol,
                Endpoints = protocol.Endpoints.Select(endpoint => new Contract.SubscriptionEndpoint {
                    Name = endpoint.Name,
                    Address = endpoint.Address.ToString(),
                    IsMain = endpoint.IsMain,
                    Connection = MapConnection(endpoint.Connection),
                    Transports = endpoint.Transports.Select(MapTransport).ToArray()
                }).ToArray()
            }).ToArray()
        };
    }

    private static Contract.ProtocolConnectionInfo MapConnection(ProtocolConnectionInfo connection) =>
        connection switch {
            WireGuardConnectionInfo wireGuard => new Contract.WireGuardConnectionInfo {
                ServerAddress = wireGuard.ServerAddress,
                ServerPort = wireGuard.ServerPort,
                PrivateKey = wireGuard.PrivateKey,
                AssignedIp = wireGuard.AssignedIp,
                ServerPublicKey = wireGuard.ServerPublicKey,
                Mtu = wireGuard.Mtu,
                IsAmneziaWg = wireGuard.IsAmneziaWg,
                Jc = wireGuard.Jc,
                Jmin = wireGuard.Jmin,
                Jmax = wireGuard.Jmax,
                S1 = wireGuard.S1,
                S2 = wireGuard.S2,
                S3 = wireGuard.S3,
                S4 = wireGuard.S4,
                H1 = wireGuard.H1,
                H2 = wireGuard.H2,
                H3 = wireGuard.H3,
                H4 = wireGuard.H4,
                I1 = wireGuard.I1,
                I2 = wireGuard.I2,
                I3 = wireGuard.I3,
                I4 = wireGuard.I4,
                I5 = wireGuard.I5
            },
            Hysteria2ConnectionInfo hysteria2 => new Contract.Hysteria2ConnectionInfo {
                ServerAddress = hysteria2.ServerAddress,
                ServerPort = hysteria2.ServerPort,
                Password = hysteria2.Password,
                ObfsType = hysteria2.ObfsType,
                ObfsPassword = hysteria2.ObfsPassword,
                Sni = hysteria2.Sni,
                PinSha256 = hysteria2.PinSha256
            },
            _ => throw new ArgumentOutOfRangeException(nameof(connection), connection, null)
        };

    private static Contract.TransportConnectionInfo MapTransport(TransportConnectionInfo transport) =>
        transport switch {
            FreeTurnConnectionInfo freeTurn => new Contract.FreeTurnConnectionInfo {
                Peer = freeTurn.Peer,
                ObfuscationProfile = freeTurn.ObfuscationProfile?.ToString(),
                ObfuscationKey = freeTurn.ObfuscationKey,
                TurnTransport = freeTurn.TurnTransport.ToString(),
                Mode = (int)freeTurn.Mode,
                Streams = freeTurn.Streams,
                ClientId = freeTurn.ClientId
            },
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
        };
}