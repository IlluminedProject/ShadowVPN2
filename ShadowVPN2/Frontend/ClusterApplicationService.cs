using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class ClusterApplicationService(
    ClusterService clusterService,
    FrontendUserContext userContext) : IClusterApplicationService {
    public async Task<string> GenerateNodeJoinTokenAsync(string name, string? domain,
        CancellationToken cancellationToken = default) {
        await userContext.RequireRoleAsync(AppRoles.Administrator, cancellationToken);
        return await clusterService.GenerateJoinTokenAsync(name, domain);
    }
}