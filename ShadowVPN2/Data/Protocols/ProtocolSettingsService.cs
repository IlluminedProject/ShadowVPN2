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
        var protocols = await GetConfigurationAsync();
        return new ProtocolsSettingsResponse {
            MainDomain = (await globalConfigService.GetAsync()).MainDomain,
            Protocols = protocols.ToList()
        };
    }

    public async Task UpdateSettingsAsync(UpdateProtocolsSettingsRequest request) {
        request.MainDomain = domainValidationService.Normalize(request.MainDomain);
        foreach (var protocol in request.Protocols)
            protocol.MainDomain = domainValidationService.Normalize(protocol.MainDomain);

        await globalConfigService.UpdateAsync(config => {
            config.MainDomain = request.MainDomain;
            config.Protocols = request.Protocols;
        });
        logger.LogInformation("Global protocol settings synchronized. Count: {Count}", request.Protocols.Count);
    }
}