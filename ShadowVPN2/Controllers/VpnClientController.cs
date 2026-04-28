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
public class VpnClientController(
    VpnClientService vpnClientService,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VpnClientResponse>>> GetClients()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var clients = await vpnClientService.GetClientsAsync(user);
        return Ok(clients.Select(MapToResponse).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<VpnClientResponse>> CreateClient([FromBody] CreateVpnClientRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var wg = request.WireGuard is not null
            ? new WireGuardClientSettings { Mtu = request.WireGuard.Mtu }
            : null;

        var client = await vpnClientService.AddClientAsync(user, request.Name, wg);
        return CreatedAtAction(nameof(GetClient), new { id = Uri.EscapeDataString(client.Id) }, MapToResponse(client));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VpnClientResponse>> GetClient(string id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var client = await vpnClientService.GetClientAsync(id, user.Id!);
        if (client is null) return NotFound();

        return Ok(MapToResponse(client));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<VpnClientResponse>> UpdateClient(string id, [FromBody] UpdateVpnClientRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var client = await vpnClientService.UpdateClientAsync(id, user.Id!, request.Name,
            request.WireGuard is not null ? new WireGuardClientSettings { Mtu = request.WireGuard.Mtu } : null);

        if (client is null) return NotFound();
        return Ok(MapToResponse(client));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteClient(string id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var deleted = await vpnClientService.DeleteClientAsync(id, user.Id!);
        if (!deleted) return NotFound();

        return NoContent();
    }

    private static VpnClientResponse MapToResponse(EntityVpnClient client) => new()
    {
        Id = client.Id,
        Name = client.Name,
        AssignedIp = client.GetAssignedIp().ToString(),
        IsEnabled = client.IsEnabled,
        CreatedAt = client.CreatedAt,
        WireGuard = client.WireGuard is not null
            ? new WireGuardClientSettingsResponse { Mtu = client.WireGuard.Mtu }
            : null
    };
}
