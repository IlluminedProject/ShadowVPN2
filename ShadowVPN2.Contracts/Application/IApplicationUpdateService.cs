using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Contracts.Application;

public interface IApplicationUpdateService {
    Task<UpdateAvailabilityResponse> CheckAsync(CancellationToken cancellationToken = default);
    Task<ClusterUpdateResponse?> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplicationUpdateHistoryResponse>>
        GetHistoryAsync(CancellationToken cancellationToken = default);

    Task<ClusterUpdateResponse> StartAsync(bool backwardCompatible, CancellationToken cancellationToken = default);
}