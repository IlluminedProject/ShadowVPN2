using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Certificates;

public sealed class CertificateSettingsService(GlobalConfigurationService globalConfigurationService) {
    public async Task<AcmeSettings> GetAsync(CancellationToken cancellationToken = default) {
        return (await globalConfigurationService.GetAsync(cancellationToken)).AcmeSettings;
    }

    public async Task UpdateAsync(UpdateAcmeSettingsRequest request, CancellationToken cancellationToken = default) {
        if (request.Enabled && string.IsNullOrWhiteSpace(request.Email))
            throw new ArgumentException("ACME email is required");
        if (request.HttpPort is <= 0 or > 65535) throw new ArgumentException("HTTP port is invalid");
        if (request.RenewalThresholdDays is <= 0 or > 89)
            throw new ArgumentException("Renewal threshold must be between 1 and 89 days");

        await globalConfigurationService.UpdateAsync(configuration => {
            configuration.AcmeSettings.Enabled = request.Enabled;
            configuration.AcmeSettings.Email = request.Email.Trim();
            configuration.AcmeSettings.UseStaging = request.UseStaging;
            configuration.AcmeSettings.HttpPort = request.HttpPort;
            configuration.AcmeSettings.RenewalThresholdDays = request.RenewalThresholdDays;
        }, cancellationToken);
    }
}