using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/node/[controller]")]
[AllowAnonymous]
public class StatusController : ControllerBase
{
    private readonly SingBoxService _singBoxService;

    public StatusController(SingBoxService singBoxService)
    {
        _singBoxService = singBoxService;
    }

    [HttpGet]
    public IActionResult GetStatus()
    {
        if (_singBoxService.IsRunning)
        {
            return Ok();
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}