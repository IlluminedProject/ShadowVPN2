using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Exceptions;

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
    public void GetStatus()
    {
        if (!_singBoxService.IsRunning)
        {
            throw new AppException(StatusCodes.Status503ServiceUnavailable, "Service unavailable");
        }
    }
}