using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClusterController(
    ClusterService clusterService,
    ClusterSettingsService clusterSettingsService,
    GlobalConfigurationService globalConfigurationService)
    : ControllerBase {
    [HttpGet("settings")]
    [Authorize(Policy = AppPermissions.Settings.View)]
    public async Task<ClusterSettingsResponse> GetSettings(CancellationToken cancellationToken) {
        return await clusterSettingsService.GetSettingsAsync(cancellationToken);
    }

    [HttpPut("settings")]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task UpdateSettings([FromBody] UpdateClusterSettingsRequest request,
        CancellationToken cancellationToken) {
        await clusterSettingsService.UpdateSettingsAsync(request, cancellationToken);
    }

    [HttpGet("awg-settings")]
    [Authorize(Policy = AppPermissions.Settings.View)]
    public async Task<AwgClusterSettings> GetAwgSettings(CancellationToken cancellationToken) {
        var awg = (await globalConfigurationService.GetAsync(cancellationToken)).AwgSettings;
        return MapAwgSettings(awg);
    }

    [HttpPut("awg-settings")]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task UpdateAwgSettings([FromBody] AwgClusterSettings request,
        CancellationToken cancellationToken) {
        await globalConfigurationService.UpdateAsync(configuration => {
            var awg = configuration.AwgSettings;
            awg.ListenPort = request.ListenPort;
            awg.H1 = request.H1;
            awg.H2 = request.H2;
            awg.H3 = request.H3;
            awg.H4 = request.H4;
            awg.Jc = request.Jc;
            awg.Jmin = request.Jmin;
            awg.Jmax = request.Jmax;
            awg.S1 = request.S1;
            awg.S2 = request.S2;
            awg.S3 = request.S3;
            awg.S4 = request.S4;
            awg.I1 = request.I1;
            awg.I2 = request.I2;
            awg.I3 = request.I3;
            awg.I4 = request.I4;
            awg.I5 = request.I5;
        }, cancellationToken);
    }

    [HttpGet("root-ca")]
    [AllowAnonymous]
    public async Task<FileContentResult> DownloadRootCa() {
        var certPem = await System.IO.File.ReadAllBytesAsync(LocalConfiguration.CertificatePemPath.Value);
        return File(certPem, "application/x-pem-file", "root-ca.crt");
    }

    [HttpPost("generate-token")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<string> GenerateToken([FromBody] GenerateNodeJoinTokenRequest request) {
        return await clusterService.GenerateJoinTokenAsync(request.Name, request.Domain);
    }

    [HttpPost("exchange-token")]
    [AllowAnonymous]
    public async Task<ClusterSignJoinResponse> ExchangeToken([FromBody] ClusterSignJoinRequest request) {
        var remoteIpAddress = HttpContext.Connection.RemoteIpAddress;
        if (remoteIpAddress is { IsIPv4MappedToIPv6: true }) remoteIpAddress = remoteIpAddress.MapToIPv4();

        var remoteIp = remoteIpAddress?.ToString();

        return await clusterService.ExchangeTokenAsync(request, remoteIp);
    }

    [HttpPost("finish-join")]
    [AllowAnonymous]
    public async Task FinishJoin([FromBody] ClusterFinishJoinRequest finishJoinRequest) {
        await clusterService.FinishJoinAsync(finishJoinRequest);
    }

    private static AwgClusterSettings MapAwgSettings(AwgGlobalSettings awg) {
        return new AwgClusterSettings {
            ListenPort = awg.ListenPort,
            H1 = awg.H1,
            H2 = awg.H2,
            H3 = awg.H3,
            H4 = awg.H4,
            Jc = awg.Jc,
            Jmin = awg.Jmin,
            Jmax = awg.Jmax,
            S1 = awg.S1,
            S2 = awg.S2,
            S3 = awg.S3,
            S4 = awg.S4,
            I1 = awg.I1,
            I2 = awg.I2,
            I3 = awg.I3,
            I4 = awg.I4,
            I5 = awg.I5
        };
    }
}