using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AppPermissions.Nodes.View)]
public sealed class NodesController(NodeService nodeService, NodeNetworkService nodeNetworkService) : ControllerBase {
    [HttpGet]
    public async Task<IReadOnlyList<NodeResponse>> Get(CancellationToken cancellationToken) {
        return await nodeService.GetNodeResponsesAsync(cancellationToken);
    }

    [HttpPut("{nodeId:guid}/domain")]
    [Authorize(Policy = AppPermissions.Nodes.Manage)]
    public async Task<NodeResponse> UpdateDomain(Guid nodeId, [FromBody] UpdateNodeDomainRequest request,
        CancellationToken cancellationToken) {
        return await nodeService.UpdateDomainAsync(nodeId, request.Domain, cancellationToken);
    }

    [HttpPost("{nodeId:guid}/check-domain")]
    public async Task<DomainCheckResponse> CheckDomain(Guid nodeId, CancellationToken cancellationToken) {
        return await nodeService.CheckDomainAsync(nodeId, cancellationToken);
    }

    [HttpPost("local/refresh-network")]
    [Authorize(Policy = AppPermissions.Nodes.Manage)]
    public async Task<EntityNodeNetworkStatus> RefreshLocalNetwork(CancellationToken cancellationToken) {
        return await nodeNetworkService.RefreshLocalAsync(cancellationToken);
    }
}