using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AppPermissions.Settings.View)]
public sealed class CertificatesController(
    ManagedCertificateService managedCertificateService,
    AcmeCertificateService acmeCertificateService,
    CertificateSettingsService certificateSettingsService,
    IOptions<LocalConfiguration> localConfiguration) : ControllerBase {
    [HttpGet("local")]
    public async Task<ManagedCertificateResponse?> GetLocal(CancellationToken cancellationToken) {
        var certificate = await managedCertificateService.GetEntityAsync(localConfiguration.Value.NodeId,
            cancellationToken);
        return certificate == null
            ? null
            : new ManagedCertificateResponse {
                NodeId = certificate.NodeId,
                Domains = certificate.Domains.AsReadOnly(),
                NotBefore = certificate.NotBefore,
                NotAfter = certificate.NotAfter,
                LastAttemptAt = certificate.LastAttemptAt,
                LastError = certificate.LastError
            };
    }

    [HttpPost("local/renew")]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task RenewLocal(CancellationToken cancellationToken) {
        await acmeCertificateService.RenewNowAsync(cancellationToken);
    }

    [HttpGet("settings")]
    public async Task<AcmeSettings> GetSettings(CancellationToken cancellationToken) {
        return await certificateSettingsService.GetAsync(cancellationToken);
    }

    [HttpPost("settings")]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task UpdateSettings([FromBody] UpdateAcmeSettingsRequest request,
        CancellationToken cancellationToken) {
        await certificateSettingsService.UpdateAsync(request, cancellationToken);
    }
}