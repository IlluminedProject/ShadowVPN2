using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Data.Subscription.Exporters;
using ShadowVPN2.Data.Subscription.Models.Connections;
using ShadowVPN2.Data.Subscription.Renderers;
using ShadowVPN2.Data.Subscription.Services;
using Contract = ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Frontend;

public sealed class SubscriptionApplicationService(ISubscriptionSource subscriptionService,
    SubscriptionProtocolExporterRegistry exporters, SubscriptionRendererRegistry renderers)
    : ISubscriptionApplicationService {
    public Task<IReadOnlyList<Contract.SubscriptionFormatDescriptor>> GetFormatsAsync(
        CancellationToken cancellationToken = default) => Task.FromResult(renderers.Formats);
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
                    Address = endpoint.Address,
                    IsMain = endpoint.IsMain,
                    PublishDirectConnection = endpoint.PublishDirectConnection,
                    Connection = exporters.GetRequired(endpoint.Connection).ToContract(endpoint.Connection),
                    ShareUrl = endpoint.PublishDirectConnection
                        ? exporters.GetRequired(endpoint.Connection).CreateShareUrl(endpoint.Connection, response.ClientName)
                        : null,
                    Transports = endpoint.Transports.Select(MapTransport).ToArray()
                }).ToArray()
            }).ToArray()
        };
    }


    private static Contract.TransportConnectionInfo MapTransport(TransportConnectionInfo transport) {
        return transport switch {
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
}