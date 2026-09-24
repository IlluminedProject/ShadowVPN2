namespace ShadowVPN2.Data.Cluster;

public sealed class ClusterSettingsService(
    GlobalConfigurationService globalConfigurationService,
    DomainValidationService domainValidationService) {
    public async Task<ClusterSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken = default) {
        var configuration = await globalConfigurationService.GetAsync(cancellationToken);
        return new ClusterSettingsResponse { GlobalDomain = configuration.GlobalDomain };
    }

    public async Task UpdateSettingsAsync(UpdateClusterSettingsRequest request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        var globalDomain = domainValidationService.Normalize(request.GlobalDomain);

        await globalConfigurationService.UpdateAsync(configuration => { configuration.GlobalDomain = globalDomain; },
            cancellationToken);
    }
}