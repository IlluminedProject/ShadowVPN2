using ShadowVPN2.Contracts.Clients;

namespace ShadowVPN2.Contracts.Application;

public interface IClientApplicationService {
    Task<IReadOnlyList<ClientResponse>> GetClientsAsync(CancellationToken cancellationToken = default);
    Task<ClientResponse> CreateClientAsync(CreateClientRequest request, CancellationToken cancellationToken = default);
    Task<ClientResponse> GetClientAsync(string id, CancellationToken cancellationToken = default);

    Task<ClientResponse> UpdateClientAsync(string id, UpdateClientRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteClientAsync(string id, CancellationToken cancellationToken = default);
}