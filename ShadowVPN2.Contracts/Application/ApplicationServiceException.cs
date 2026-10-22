using System.Net;

namespace ShadowVPN2.Contracts.Application;

public sealed class ApplicationServiceException : Exception {
    public ApplicationServiceException(HttpStatusCode statusCode) {
        StatusCode = statusCode;
    }

    public ApplicationServiceException(HttpStatusCode statusCode, string message) : base(message) {
        StatusCode = statusCode;
    }

    public ApplicationServiceException(HttpStatusCode statusCode, string message, Exception innerException)
        : base(message, innerException) {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}