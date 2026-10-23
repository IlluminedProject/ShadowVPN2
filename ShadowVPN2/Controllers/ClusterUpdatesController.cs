using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Raven.Client.Documents;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/cluster/updates")]
public sealed class ClusterUpdatesController(
    ClusterUpdateService updateService,
    IDocumentStore documentStore) : ControllerBase {
    [HttpPost]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task<ClusterUpdateResponse> Start([FromBody] StartClusterUpdateRequest request,
        CancellationToken cancellationToken) {
        var topology = await documentStore.Maintenance.Server.SendAsync(new GetClusterTopologyOperation(),
            cancellationToken);
        return await updateService.StartAsync(request, topology, topology.NodeTag,
            cancellationToken);
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.Settings.View)]
    public async Task<ClusterUpdateResponse?> Get(CancellationToken cancellationToken) {
        return await updateService.GetAsync(cancellationToken);
    }

    [HttpGet("availability")]
    [Authorize(Policy = AppPermissions.Settings.View)]
    public Task<UpdateAvailabilityResponse> Availability(
        [FromServices] ApplicationUpdateService applicationUpdates, CancellationToken cancellationToken) {
        return applicationUpdates.CheckAsync(cancellationToken);
    }

    [HttpGet("history")]
    [Authorize(Policy = AppPermissions.Settings.View)]
    public Task<IReadOnlyList<ApplicationUpdateHistoryResponse>> History(CancellationToken cancellationToken) {
        return updateService.GetHistoryAsync(cancellationToken);
    }

    [HttpPost("start-available")]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task<ActionResult<ClusterUpdateResponse>> StartAvailable(
        [FromBody] StartAvailableUpdateRequest request,
        [FromServices] ApplicationUpdateService applicationUpdates, CancellationToken cancellationToken) {
        return await applicationUpdates.StartAsync(request.BackwardCompatible, cancellationToken);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task<ClusterUpdateResponse> Cancel(Guid id, CancellationToken cancellationToken) {
        return await updateService.CancelAsync(id, cancellationToken);
    }
}