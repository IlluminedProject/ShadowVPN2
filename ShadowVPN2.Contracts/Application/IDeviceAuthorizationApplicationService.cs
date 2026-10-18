using ShadowVPN2.Contracts.Auth;

namespace ShadowVPN2.Contracts.Application;

public interface IDeviceAuthorizationApplicationService {
    Task<DeviceAuthorizationStartResponse> GetDetailsAsync(string transactionId,
        CancellationToken cancellationToken = default);

    Task<DeviceAuthorizationPollResponse> PollAsync(string transactionId,
        CancellationToken cancellationToken = default);
}