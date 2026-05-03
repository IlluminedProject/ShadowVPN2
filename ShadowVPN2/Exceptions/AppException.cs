namespace ShadowVPN2.Exceptions;

public class AppException : Exception
{
    public AppException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}