using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController(SetupService setupService) : ControllerBase
{
    [HttpGet("test-oidc")]
    public async Task<bool> TestOidc([FromQuery] string authority)
    {
        return await setupService.TestOidcConnectionAsync(authority);
    }
}
