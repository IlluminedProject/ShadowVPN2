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

    [HttpPost("auth/local")]
    public async Task ConfigureLocalAuth([FromBody] LocalAuthSetupRequest request)
    {
        await setupService.ConfigureLocalAuthAsync(request);
    }

    [HttpPost("auth/oidc")]
    public async Task ConfigureOidc([FromBody] OidcAuthSetupRequest request)
    {
        await setupService.ConfigureOidcAsync(request);
    }

    [HttpGet("root-ca")]
    public async Task<IActionResult> DownloadRootCa()
    {
        var bytes = await setupService.GetRootCaBytesAsync();
        return File(bytes, "application/x-pkcs12", "root-ca.pfx");
    }

    [HttpPost("finish")]
    public async Task FinishSetup()
    {
        await setupService.FinishSetupAsync();
    }
}