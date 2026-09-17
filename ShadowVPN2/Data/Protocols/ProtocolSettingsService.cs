using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Protocols;

public class ProtocolSettingsService(
    GlobalConfigurationService globalConfigService,
    DomainValidationService domainValidationService,
    ILogger<ProtocolSettingsService> logger) {
    public async Task<IReadOnlyList<ProtocolGlobalSettings>> GetConfigurationAsync() {
        var config = await globalConfigService.GetAsync();
        return config.Protocols;
    }

    public async Task<ProtocolsSettingsResponse> GetSettingsAsync() {
        var config = await globalConfigService.GetAsync();
        return new ProtocolsSettingsResponse {
            MainDomain = config.MainDomain,
            Protocols = config.Protocols.ToList(),
            Transports = config.Transports.ToList()
        };
    }

    public async Task UpdateSettingsAsync(UpdateProtocolsSettingsRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        request.MainDomain = domainValidationService.Normalize(request.MainDomain);
        foreach (var protocol in request.Protocols)
            protocol.MainDomain = domainValidationService.Normalize(protocol.MainDomain);

        var configuration = new EntityGlobalConfiguration {
            MainDomain = request.MainDomain,
            Protocols = request.Protocols,
            Transports = request.Transports
        };
        GlobalConfigurationValidator.Validate(configuration);

        await globalConfigService.UpdateAsync(config => {
            config.MainDomain = request.MainDomain;
            config.Protocols = request.Protocols;
            config.Transports = request.Transports;
        });
        logger.LogInformation("Global protocol settings synchronized. Count: {Count}", request.Protocols.Count);
    }
}