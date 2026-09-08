using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth/device")]
public sealed class DeviceAuthorizationController(DeviceAuthorizationService deviceService) : ControllerBase {
    [HttpPost("start")]
    public async Task<DeviceAuthorizationStartResponse> Start(CancellationToken cancellationToken) {
        var result = await deviceService.StartAsync(cancellationToken);
        SetCorrelationCookie(result.CorrelationSecret);
        return result.Response;
    }

    [HttpGet("start-page")]
    public async Task<RedirectHttpResult> StartPage([FromQuery] string? returnUrl,
        CancellationToken cancellationToken) {
        var result = await deviceService.StartAsync(cancellationToken);
        SetCorrelationCookie(result.CorrelationSecret);

        var location = QueryString.Create(new Dictionary<string, string?> {
            ["transactionId"] = result.Response.TransactionId,
            ["returnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : "/"
        });
        return TypedResults.Redirect($"/Account/DeviceLogin{location}");
    }

    [HttpPost("poll/{transactionId}")]
    public async Task<DeviceAuthorizationPollResponse> Poll(string transactionId, CancellationToken cancellationToken) {
        var secret = Request.Cookies[DeviceAuthorizationService.CorrelationCookie]
                     ?? throw new UnauthorizedAccessException("Device authorization session is missing.");
        return await deviceService.PollAsync(transactionId, secret, cancellationToken);
    }

    [HttpGet("complete/{transactionId}")]
    public async Task<IResult> Complete(string transactionId, [FromQuery] string? returnUrl,
        CancellationToken cancellationToken) {
        var secret = Request.Cookies[DeviceAuthorizationService.CorrelationCookie];
        if (string.IsNullOrWhiteSpace(secret))
            return TypedResults.BadRequest("Device authorization session is missing.");

        var result = await deviceService.CompleteAsync(transactionId, secret, cancellationToken);
        if (result.Status == "completed") {
            Response.Cookies.Delete(DeviceAuthorizationService.CorrelationCookie);
            return TypedResults.LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        return TypedResults.BadRequest(result.Error ?? $"Device authorization status: {result.Status}");
    }

    private void SetCorrelationCookie(string secret) {
        Response.Cookies.Append(DeviceAuthorizationService.CorrelationCookie, secret,
            new CookieOptions {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                MaxAge = TimeSpan.FromMinutes(15)
            });
    }
}