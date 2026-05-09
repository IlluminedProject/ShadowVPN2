using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.Protocols;

public class ProtocolSettingsService(
    GlobalConfigurationService globalConfigService,
    ILogger<ProtocolSettingsService> logger)
{
    public async Task<IReadOnlyList<ProtocolGlobalSettings>> GetConfigurationAsync()
    {
        var config = await globalConfigService.GetAsync();
        return config.Protocols;
    }

    public async Task<ProtocolsSettingsResponse> GetSettingsAsync()
    {
        var protocols = await GetConfigurationAsync();
        return new ProtocolsSettingsResponse
        {
            Protocols = protocols.ToList()
        };
    }

    public async Task UpdateSettingsAsync(UpdateProtocolsSettingsRequest request)
    {
        await globalConfigService.UpdateAsync(config => { config.Protocols = request.Protocols; });
        logger.LogInformation("Global protocol settings synchronized. Count: {Count}", request.Protocols.Count);
    }
}