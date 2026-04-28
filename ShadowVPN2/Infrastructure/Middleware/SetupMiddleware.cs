using ShadowVPN2.Data;

namespace ShadowVPN2.Infrastructure.Middleware;

public class SetupMiddleware
{
    private readonly RequestDelegate _next;

    public SetupMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, SetupService setupService)
    {
        var path = context.Request.Path.Value;

        // Skip static files, framework files
        if (path != null && (
                path.StartsWith("/_") ||
                path.StartsWith("/static") ||
                path.StartsWith("/css") ||
                path.StartsWith("/js") ||
                path.StartsWith("/images") ||
                path.StartsWith("/favicon.ico") ||
                path.StartsWith("/lib")))
        {
            await _next(context);
            return;
        }

        var needsSetup = await setupService.NeedsSetupAsync();
        var isApi = path != null && path.StartsWith("/api/");
        var isSetupRoute = path == "/setup" || (path != null && path.StartsWith("/api/setup"));
        var isStatusRoute = path != null && path.StartsWith("/api/node/status");

        if (needsSetup)
        {
            // If setup is needed, block access to everything except setup routes and status route
            if (!isSetupRoute && !isStatusRoute)
            {
                if (isApi)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { message = "Initial setup is required." });
                    return;
                }

                context.Response.Redirect("/setup");
                return;
            }
        }
        else
        {
            // If setup is complete, block access to setup routes
            if (isSetupRoute)
            {
                if (isApi)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { message = "Setup is already completed." });
                    return;
                }

                context.Response.Redirect("/");
                return;
            }
        }

        await _next(context);
    }
}
