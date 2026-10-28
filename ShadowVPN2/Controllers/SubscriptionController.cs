using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Infrastructure.Extensions;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class SubscriptionController(ISubscriptionApplicationService subscriptionService) : ControllerBase {
    [HttpGet("{id:guid}")]
    public async Task<SubscriptionResponse> GetSubscription(Guid id, CancellationToken cancellationToken) {
        var response = await subscriptionService.GetSubscriptionAsync(id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return response.OrThrowNotFound("Subscription not found");
    }
}
