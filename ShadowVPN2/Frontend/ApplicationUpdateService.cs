using System.Net;
using Raven.Client.Documents;
using ShadowVPN2.Entities;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Exceptions;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Frontend;

public sealed class ApplicationUpdateService(
    Data.Cluster.ApplicationUpdateService updates,
    IDocumentStore documentStore,
    FrontendUserContext userContext,
    ILogger<ApplicationUpdateService> logger) : IApplicationUpdateService {
    public async Task<UpdateAvailabilityResponse> CheckAsync(CancellationToken cancellationToken = default,
        bool forceRefresh = false) {
        await userContext.RequirePolicyAsync(AppPermissions.Settings.View, cancellationToken);
        return await updates.CheckAsync(cancellationToken, forceRefresh);
    }

    public async Task<ClusterUpdateResponse?> GetStatusAsync(CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Settings.View, cancellationToken);
        return await AddNodeMetadataAsync(await updates.GetStatusAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<ApplicationUpdateHistoryResponse>> GetHistoryAsync(
        CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Settings.View, cancellationToken);
        return await updates.GetHistoryAsync(cancellationToken);
    }

    public async Task<ClusterUpdateResponse> StartAsync(bool backwardCompatible,
        CancellationToken cancellationToken = default) {
        await userContext.RequirePolicyAsync(AppPermissions.Settings.Manage, cancellationToken);
        try {
            var status = await updates.StartAsync(backwardCompatible, cancellationToken);
            return (await AddNodeMetadataAsync(status, cancellationToken))!;
        }
        catch (AppException exception) when (exception.StatusCode == StatusCodes.Status409Conflict) {
            logger.LogWarning(exception, "Cluster update start rejected: {Reason}", exception.Message);
            throw new ApplicationServiceException(HttpStatusCode.Conflict, exception.Message, exception);
        }
    }

    private async Task<ClusterUpdateResponse?> AddNodeMetadataAsync(ClusterUpdateResponse? status,
        CancellationToken cancellationToken) {
        if (status is null || status.Nodes.Count == 0) return status;
        try {
            var topology = await documentStore.Maintenance.Server.SendAsync(
                new Data.Cluster.GetClusterTopologyOperation(), cancellationToken);
            using var session = documentStore.OpenAsyncSession();
            var nodes = await session.Query<EntityClusterNode>().ToListAsync(cancellationToken);
            foreach (var node in status.Nodes) {
                if (!topology.Topology.Members.TryGetValue(node.NodeTag, out var url) ||
                    !Uri.TryCreate(url, UriKind.Absolute, out var address))
                    continue;
                var configured = nodes.FirstOrDefault(candidate =>
                    string.Equals(candidate.AwgMeshIp, address.Host, StringComparison.OrdinalIgnoreCase));
                node.NodeName = configured?.Name;
                node.NodeAddress = configured?.Domain?.ToString() ?? address.Host;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception exception) {
            logger.LogWarning(exception, "Could not load node display metadata for update {UpdateId}", status.Id);
        }

        return status;
    }
}