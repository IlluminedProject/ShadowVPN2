using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Exceptions;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/node/[controller]")]
[AllowAnonymous]
public class StatusController(SingBoxService singBoxService) : ControllerBase {
    [HttpGet]
    public void GetStatus() {
        Response.Headers.AccessControlAllowOrigin = "*";

        if (!singBoxService.IsRunning) {
            throw new AppException(StatusCodes.Status503ServiceUnavailable, "Service unavailable");
        }
    }
}