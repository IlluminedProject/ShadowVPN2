using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AppPermissions.Settings.View)]
public class ProtocolsController(ProtocolSettingsService protocolSettingsService) : ControllerBase
{
    [HttpGet]
    public async Task<ProtocolsSettingsResponse> Get()
    {
        return await protocolSettingsService.GetSettingsAsync();
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task Save([FromBody] UpdateProtocolsSettingsRequest request)
    {
        await protocolSettingsService.UpdateSettingsAsync(request);
    }
}