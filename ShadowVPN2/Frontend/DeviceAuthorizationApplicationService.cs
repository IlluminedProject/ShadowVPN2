using System.Net;
using Microsoft.AspNetCore.Components.Server.Circuits;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class DeviceAuthorizationApplicationService(
    DeviceAuthorizationService deviceService,
    IHttpContextAccessor httpContextAccessor) : CircuitHandler, IDeviceAuthorizationApplicationService {
    private string? _correlationSecret;

    public async Task<DeviceAuthorizationStartResponse> GetDetailsAsync(string transactionId,
        CancellationToken cancellationToken = default) {
        var result = await deviceService.GetStartResponseAsync(transactionId, RequireCorrelation(), cancellationToken);
        return new DeviceAuthorizationStartResponse {
            TransactionId = result.TransactionId,
            VerificationUri = result.VerificationUri,
            VerificationUriComplete = result.VerificationUriComplete,
            UserCode = result.UserCode,
            ExpiresIn = result.ExpiresIn,
            Interval = result.Interval
        };
    }

    public async Task<DeviceAuthorizationPollResponse> PollAsync(string transactionId,
        CancellationToken cancellationToken = default) {
        var result = await deviceService.PollAsync(transactionId, RequireCorrelation(), cancellationToken);
        return new DeviceAuthorizationPollResponse {
            Status = result.Status,
            RetryAfter = result.RetryAfter,
            Error = result.Error
        };
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) {
        _correlationSecret = httpContextAccessor.HttpContext?.Request.Cookies[
            DeviceAuthorizationService.CorrelationCookie];
        return Task.CompletedTask;
    }

    private string RequireCorrelation() {
        return _correlationSecret
               ?? throw new ApplicationServiceException(HttpStatusCode.Unauthorized);
    }
}