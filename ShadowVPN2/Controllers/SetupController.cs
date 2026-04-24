using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class SetupController(SetupService setupService) : ControllerBase
{
    [HttpGet("public-ip")]
    public async Task<string> GetPublicIp()
    {
        var ip = await setupService.GetPublicIpAsync();
        return ip ?? throw new InvalidOperationException("Unable to determine public IP");
    }

    [HttpPost("node")]
    public async Task ConfigureNode([FromBody] NodeSetupRequest request)
    {
        await setupService.ConfigureNodeAsync(request);
    }

    [HttpPost("admin")]
    public async Task CreateAdmin([FromBody] AdminSetupRequest request)
    {
        await setupService.CreateAdminAsync(request);
    }
}