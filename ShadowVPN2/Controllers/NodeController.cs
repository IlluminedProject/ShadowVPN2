using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AppPermissions.Nodes.View)]
public class NodeController(NodeService nodeService) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<NodeResponse>> GetNodes()
    {
        var nodes = await nodeService.GetNodesAsync();
        return nodes.Select(n => new NodeResponse
        {
            Id = n.Id,
            NodeId = n.NodeId,
            Name = n.Name,
            Address = n.Address,
            Number = n.Number
        });
    }
}
