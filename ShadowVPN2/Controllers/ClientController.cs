using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClientController(
    ClientService clientService,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientResponse>>> GetClients()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var clients = await clientService.GetClientsAsync(user);
        return Ok(clients.Select(ClientMapper.MapToResponse).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ClientResponse>> CreateClient([FromBody] CreateClientRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var wg = request.WireGuard is not null
            ? new WireGuardClientSettings { Mtu = request.WireGuard.Mtu }
            : null;

        var client = await clientService.AddClientAsync(user, request.Name, wg);
        return CreatedAtAction(nameof(GetClient), new { id = Uri.EscapeDataString(client.Id) }, ClientMapper.MapToResponse(client));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ClientResponse>> GetClient(string id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var client = await clientService.GetClientAsync(id, user.Id!);
        if (client is null) return NotFound();

        return Ok(ClientMapper.MapToResponse(client));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ClientResponse>> UpdateClient(string id, [FromBody] UpdateClientRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var client = await clientService.UpdateClientAsync(id, user.Id!, request.Name, request.IsEnabled,
            request.WireGuard is not null ? new WireGuardClientSettings { Mtu = request.WireGuard.Mtu } : null);

        if (client is null) return NotFound();
        return Ok(ClientMapper.MapToResponse(client));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteClient(string id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var deleted = await clientService.DeleteClientAsync(id, user.Id!);
        if (!deleted) return NotFound();

        return NoContent();
    }

    private static ClientResponse MapToResponse(EntityClient client) => ClientMapper.MapToResponse(client);
}
