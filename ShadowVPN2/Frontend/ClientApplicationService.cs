using System.Net;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Contracts.Clients;
using ShadowVPN2.Data;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Frontend;

public sealed class ClientApplicationService(
    ClientService clientService,
    FrontendUserContext userContext) : IClientApplicationService {
    public async Task<IReadOnlyList<ClientResponse>> GetClientsAsync(CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        return (await clientService.GetClientsAsync(user, cancellationToken)).Select(ClientMapper.MapToResponse)
            .ToArray();
    }

    public async Task<ClientResponse> CreateClientAsync(CreateClientRequest request,
        CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        var wireGuard = request.WireGuard is null ? null : new WireGuardClientSettings { Mtu = request.WireGuard.Mtu };
        var client = await clientService.AddClientAsync(user, request.Name, wireGuard, cancellationToken);
        return ClientMapper.MapToResponse(client);
    }

    public async Task<ClientResponse> GetClientAsync(string id, CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        var client = await clientService.GetClientAsync(id, user.Id!, cancellationToken);
        return ClientMapper.MapToResponse(client ?? throw new ApplicationServiceException(HttpStatusCode.NotFound));
    }

    public async Task<ClientResponse> UpdateClientAsync(string id, UpdateClientRequest request,
        CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        var wireGuard = request.WireGuard is null ? null : new WireGuardClientSettings { Mtu = request.WireGuard.Mtu };
        var client = await clientService.UpdateClientAsync(id, user.Id!, request.Name, request.IsEnabled,
            wireGuard, cancellationToken);
        return ClientMapper.MapToResponse(client ?? throw new ApplicationServiceException(HttpStatusCode.NotFound));
    }

    public async Task DeleteClientAsync(string id, CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        if (!await clientService.DeleteClientAsync(id, user.Id!, cancellationToken))
            throw new ApplicationServiceException(HttpStatusCode.NotFound);
    }
}