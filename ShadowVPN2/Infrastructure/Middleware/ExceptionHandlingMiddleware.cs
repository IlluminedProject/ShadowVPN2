using System.Text.Json;
using ShadowVPN2.Exceptions;

namespace ShadowVPN2.Infrastructure.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger) {
    public async Task InvokeAsync(HttpContext context) {
        try {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            var statusCode = ex switch {
                UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
                KeyNotFoundException => StatusCodes.Status404NotFound,
                ArgumentException => StatusCodes.Status400BadRequest,
                AppException appEx => appEx.StatusCode,
                _ => StatusCodes.Status500InternalServerError
            };

            if (statusCode >= 500) logger.LogError(ex, "An application error occurred");

            if (context.Response.HasStarted) {
                logger.LogWarning(ex,
                    "The response has already started, the exception middleware will not be executed");
                throw;
            }

            context.Response.Clear();
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            var message = statusCode >= 500 ? "An internal server error occurred." : ex.Message;

            // For UnauthorizedAccessException, the default message is often long,
            // so we can customize it or just use ex.Message.
            var result = JsonSerializer.Serialize(new { error = message });
            await context.Response.WriteAsync(result);
        }
    }
}