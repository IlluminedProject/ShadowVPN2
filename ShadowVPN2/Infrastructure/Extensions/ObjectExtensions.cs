namespace ShadowVPN2.Infrastructure.Extensions;

public static class ObjectExtensions
{
    public static T OrThrowNotFound<T>(this T? value, string message = "Resource not found") where T : class
    {
        return value ?? throw new KeyNotFoundException(message);
    }

    public static T OrThrowNotFound<T>(this T? value, string message = "Resource not found") where T : struct
    {
        return value ?? throw new KeyNotFoundException(message);
    }
}