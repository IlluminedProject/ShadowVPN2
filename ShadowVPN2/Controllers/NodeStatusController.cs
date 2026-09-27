using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/node/[controller]")]
[AllowAnonymous]
public class StatusController(SingBoxService singBoxService) : ControllerBase {
    [HttpGet]
    public void GetStatus() {
        Response.Headers.AccessControlAllowOrigin = "*";

        if (!singBoxService.IsRunning) {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
    }
}