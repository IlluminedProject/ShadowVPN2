using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Contracts.Clients;
using ShadowVPN2.Data;

namespace ShadowVPN2.Frontend;

public sealed class ClientUpdatesService(
    ClientService clientService,
    FrontendUserContext userContext) : IClientUpdatesService {
    public async Task<IAsyncDisposable> SubscribeAsync(Func<IReadOnlyList<ClientResponse>, Task> onUpdate,
        CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        var subscription = await clientService.SubscribeAsync(user, clients =>
            onUpdate(clients.Select(ClientMapper.MapToResponse).ToArray()));
        return new Subscription(subscription);
    }

    private sealed class Subscription(ClientService.ClientSubscription subscription) : IAsyncDisposable {
        public ValueTask DisposeAsync() {
            subscription.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}