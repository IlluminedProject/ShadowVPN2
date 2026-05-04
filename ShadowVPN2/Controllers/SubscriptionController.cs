using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Infrastructure.Extensions;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class SubscriptionController(SubscriptionService subscriptionService) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<SubscriptionResponse> GetSubscription(Guid id)
    {
        var response = await subscriptionService.GetSubscriptionAsync(id);
        return response.OrThrowNotFound("Subscription not found");
    }
}