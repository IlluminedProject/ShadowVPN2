namespace ShadowVPN2.Exceptions;

public class AppException(int statusCode, string message) : Exception(message) {
    public int StatusCode { get; } = statusCode;
}