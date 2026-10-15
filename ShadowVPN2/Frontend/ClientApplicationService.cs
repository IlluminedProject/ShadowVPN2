using System.Net;
using Microsoft.AspNetCore.Identity;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Contracts.Clients;
using ShadowVPN2.Data;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class ClientApplicationService(
    ClientService clientService,
    FrontendUserContext userContext,
    UserManager<ApplicationUser> userManager) : IClientApplicationService {
    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) {
        return userContext.IsInRoleAsync(AppRoles.Administrator);
    }

    public async Task<IReadOnlyList<ClientResponse>> GetClientsAsync(CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        var isAdministrator = await userContext.IsInRoleAsync(AppRoles.Administrator);
        var clients = await clientService.GetClientsAsync(user, isAdministrator, cancellationToken);
        var ownerNames = await GetOwnerNamesAsync(clients, cancellationToken);
        return clients
            .Select(client => ClientMapper.MapToResponse(client, user.Id,
                ownerNames.GetValueOrDefault(client.UserId)))
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
        var isAdministrator = await userContext.IsInRoleAsync(AppRoles.Administrator);
        var client = await clientService.GetClientAsync(id, user.Id!, isAdministrator, cancellationToken);
        var entity = client ?? throw new ApplicationServiceException(HttpStatusCode.NotFound);
        var owner = await userManager.FindByIdAsync(entity.UserId);
        return ClientMapper.MapToResponse(entity, user.Id, GetOwnerDisplayName(owner));
    }

    public async Task<ClientResponse> UpdateClientAsync(string id, UpdateClientRequest request,
        CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        var isAdministrator = await userContext.IsInRoleAsync(AppRoles.Administrator);
        var wireGuard = request.WireGuard is null ? null : new WireGuardClientSettings { Mtu = request.WireGuard.Mtu };
        var client = await clientService.UpdateClientAsync(id, user.Id!, isAdministrator, request.Name,
            request.IsEnabled,
            wireGuard, cancellationToken);
        return ClientMapper.MapToResponse(client ?? throw new ApplicationServiceException(HttpStatusCode.NotFound),
            user.Id);
    }

    public async Task DeleteClientAsync(string id, CancellationToken cancellationToken = default) {
        var user = await userContext.RequireUserAsync(cancellationToken);
        var isAdministrator = await userContext.IsInRoleAsync(AppRoles.Administrator);
        if (!await clientService.DeleteClientAsync(id, user.Id!, isAdministrator, cancellationToken))
            throw new ApplicationServiceException(HttpStatusCode.NotFound);
    }

    private async Task<Dictionary<string, string>> GetOwnerNamesAsync(
        IReadOnlyList<EntityClient> clients, CancellationToken cancellationToken) {
        if (clients.Count == 0)
            return new Dictionary<string, string>();

        var userIds = clients.Select(client => client.UserId).Distinct().ToArray();
        var result = new Dictionary<string, string>();
        var owners = new Dictionary<string, ApplicationUser>();
        foreach (var userId in userIds) {
            var owner = await userManager.FindByIdAsync(userId);
            if (owner is not null)
                owners[userId] = owner;
        }

        var duplicateNames = owners.Values
            .Where(owner => !string.IsNullOrWhiteSpace(owner.DisplayName))
            .GroupBy(owner => owner.DisplayName!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var userId in userIds) {
            owners.TryGetValue(userId, out var owner);
            var name = GetOwnerDisplayName(owner);
            result[userId] = owner?.DisplayName is not null && duplicateNames.Contains(owner.DisplayName)
                ? $"{name} ({owner.UserName})"
                : name;
        }

        return result;
    }

    private static string GetOwnerDisplayName(ApplicationUser? user) {
        return string.IsNullOrWhiteSpace(user?.DisplayName) ? $"User #{user?.UserNumber}" : user.DisplayName;
    }
}