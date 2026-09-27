using ShadowVPN2.Contracts.Clients;

namespace ShadowVPN2.Contracts.Application;

public interface IClientUpdatesService {
    Task<IAsyncDisposable> SubscribeAsync(Func<IReadOnlyList<ClientResponse>, Task> onUpdate,
        CancellationToken cancellationToken = default);
}