using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Extensions;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClientController(
    ClientService clientService,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ClientResponse>> GetClients()
    {
        var user = await userManager.GetRequiredUserAsync(User);

        var clients = await clientService.GetClientsAsync(user);
        return clients.Select(ClientMapper.MapToResponse).ToList();
    }

    [HttpPost]
    public async Task<ClientResponse> CreateClient([FromBody] CreateClientRequest request)
    {
        var user = await userManager.GetRequiredUserAsync(User);

        var wg = request.WireGuard is not null
            ? new WireGuardClientSettings { Mtu = request.WireGuard.Mtu }
            : null;

        var client = await clientService.AddClientAsync(user, request.Name, wg);

        Response.StatusCode = StatusCodes.Status201Created;
        return ClientMapper.MapToResponse(client);
    }

    [HttpGet("{id}")]
    public async Task<ClientResponse> GetClient(string id)
    {
        var user = await userManager.GetRequiredUserAsync(User);

        var client = await clientService.GetClientAsync(id, user.Id!);
        return ClientMapper.MapToResponse(client.OrThrowNotFound("Client not found"));
    }

    [HttpPut("{id}")]
    public async Task<ClientResponse> UpdateClient(string id, [FromBody] UpdateClientRequest request)
    {
        var user = await userManager.GetRequiredUserAsync(User);

        var client = await clientService.UpdateClientAsync(id, user.Id!, request.Name, request.IsEnabled,
            request.WireGuard is not null ? new WireGuardClientSettings { Mtu = request.WireGuard.Mtu } : null);

        return ClientMapper.MapToResponse(client.OrThrowNotFound("Client not found"));
    }

    [HttpDelete("{id}")]
    public async Task DeleteClient(string id)
    {
        var user = await userManager.GetRequiredUserAsync(User);

        var deleted = await clientService.DeleteClientAsync(id, user.Id!);
        if (!deleted) throw new KeyNotFoundException("Client not found");

        Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static ClientResponse MapToResponse(EntityClient client) => ClientMapper.MapToResponse(client);
}