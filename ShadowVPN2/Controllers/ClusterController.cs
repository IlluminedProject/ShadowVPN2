using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClusterController(ClusterService clusterService) : ControllerBase
{
    [HttpGet("root-ca")]
    [AllowAnonymous]
    public async Task<FileContentResult> DownloadRootCa()
    {
        var certPem = await System.IO.File.ReadAllBytesAsync(LocalConfiguration.CertificatePemPath.Value);
        return File(certPem, "application/x-pem-file", "root-ca.crt");
    }

    [HttpPost("generate-token")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<string> GenerateToken([FromBody] GenerateTokenRequest request)
    {
        return await clusterService.GenerateJoinTokenAsync(request.Name, request.ExternalAddress);
    }

    [HttpPost("exchange-token")]
    [AllowAnonymous]
    public async Task<ClusterSignJoinResponse> ExchangeToken([FromBody] ClusterSignJoinRequest request)
    {
        var remoteIpAddress = HttpContext.Connection.RemoteIpAddress;
        if (remoteIpAddress is { IsIPv4MappedToIPv6: true }) remoteIpAddress = remoteIpAddress.MapToIPv4();

        var remoteIp = remoteIpAddress?.ToString();

        return await clusterService.ExchangeTokenAsync(request, remoteIp);
    }

    [HttpPost("finish-join")]
    [AllowAnonymous]
    public async Task FinishJoin([FromBody] ClusterFinishJoinRequest finishJoinRequest)
    {
        await clusterService.FinishJoinAsync(finishJoinRequest);
    }
}

public class GenerateTokenRequest
{
    public required string Name { get; set; }
    public string? ExternalAddress { get; set; }
}