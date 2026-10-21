using System.Net;

namespace ShadowVPN2.Contracts.Application;

public sealed class ApplicationServiceException(HttpStatusCode statusCode) : Exception {
    private readonly string? _message;

    public ApplicationServiceException(HttpStatusCode statusCode, string message) : this(statusCode) {
        _message = message;
    }

    public HttpStatusCode StatusCode { get; } = statusCode;

    public override string Message {
        get => _message ?? base.Message;
    }
}