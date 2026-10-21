using System.Net;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class ApplicationUpdateService(
    Data.Cluster.ApplicationUpdateService updates,
    FrontendUserContext userContext) : IApplicationUpdateService {
    public async Task<UpdateAvailabilityResponse> CheckAsync(CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Settings.View, cancellationToken);
        return await updates.CheckAsync(cancellationToken);
    }

    public async Task<ClusterUpdateResponse?> GetStatusAsync(CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Settings.View, cancellationToken);
        return await updates.GetStatusAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApplicationUpdateHistoryResponse>> GetHistoryAsync(
        CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Settings.View, cancellationToken);
        return await updates.GetHistoryAsync(cancellationToken);
    }

    public async Task<ClusterUpdateResponse> StartAsync(bool backwardCompatible,
        CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Settings.Manage, cancellationToken);
        try { return await updates.StartAsync(backwardCompatible, cancellationToken); }
        catch (InvalidOperationException exception) {
            throw new ApplicationServiceException(HttpStatusCode.Conflict, exception.Message);
        }
    }
}