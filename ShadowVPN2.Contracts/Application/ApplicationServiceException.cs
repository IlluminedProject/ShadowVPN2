using System.Net;

namespace ShadowVPN2.Contracts.Application;

public sealed class ApplicationServiceException(HttpStatusCode statusCode) : Exception {
    public HttpStatusCode StatusCode { get; } = statusCode;
}