using Microsoft.AspNetCore.Diagnostics;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Data.Subscription.Models.Rules;
using ShadowVPN2.Data.Subscription.Renderers;
using ShadowVPN2.Data.Subscription.Rules;
using ShadowVPN2.Data.Subscription.Services;

namespace ShadowVPN2.Infrastructure.Middleware;

public sealed class SubscriptionResponseMiddleware(RequestDelegate next) {
    public async Task InvokeAsync(HttpContext context, SubscriptionRendererRegistry renderers,
        ISubscriptionSource subscriptions) {
        if (!TryParsePath(context.Request.Path, out var id, out var explicitFormat)) {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Vary = "*";
        var headers = context.Request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
        var responseType = SubscriptionResponseRulesMatcher.Select(headers);
        if (explicitFormat is null && responseType == SubscriptionResponseType.Browser) {
            await next(context);
            return;
        }

        // Subscription errors must not be re-executed as HTML pages by the panel's status-code middleware.
        if (context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
            statusCodePages.Enabled = false;
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET, HEAD";
            return;
        }
        if (explicitFormat is null && responseType == SubscriptionResponseType.Block) {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (explicitFormat is null && responseType == SubscriptionResponseType.NotFound) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var selectedType = explicitFormat is null ? responseType : SubscriptionFormatParser.Parse(explicitFormat);
        if (selectedType is null || !renderers.TryGet(selectedType.Value, out var renderer)) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var subscription = await subscriptions.GetSubscriptionAsync(id);
        if (subscription is null) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        string content;
        try {
            content = renderer.Render(subscription);
        }
        catch (ArgumentException exception) {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            context.Response.ContentType = "text/plain; charset=utf-8";
            if (!HttpMethods.IsHead(context.Request.Method))
                await context.Response.WriteAsync(exception.Message, context.RequestAborted);
            return;
        }
        context.Response.Headers["profile-title"] = "base64:" +
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(subscription.ClientName));
        context.Response.Headers["profile-web-page-url"] = Microsoft.AspNetCore.Http.Extensions.UriHelper.BuildAbsolute(
            context.Request.Scheme, context.Request.Host, context.Request.PathBase, $"/s/{id}");
        context.Response.ContentType = renderer.Descriptor.ContentType;
        context.Response.ContentLength = System.Text.Encoding.UTF8.GetByteCount(content);
        if (!HttpMethods.IsHead(context.Request.Method))
            await context.Response.WriteAsync(content, context.RequestAborted);
    }

    private static bool TryParsePath(PathString path, out Guid id, out string? format) {
        id = Guid.Empty;
        format = null;
        if (!path.StartsWithSegments("/s", StringComparison.OrdinalIgnoreCase, out var remainder))
            return false;
        var value = remainder.Value.AsSpan().Trim('/');
        var separator = value.IndexOf('/');
        if (separator < 0)
            return Guid.TryParse(value, out id);
        if (!Guid.TryParse(value[..separator], out id))
            return false;
        format = value[(separator + 1)..].ToString();
        return true;
    }
}
