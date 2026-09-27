using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Data;

namespace ShadowVPN2.Frontend;

public sealed class SetupApplicationService(SetupService setupService) : ISetupApplicationService {
    public Task<string?> GetPublicIpAsync(CancellationToken cancellationToken = default) =>
        setupService.GetPublicIpAsync();

    public Task ConfigureNodeAsync(NodeSetupRequest request, CancellationToken cancellationToken = default) =>
        setupService.ConfigureNodeAsync(request);

    public Task ConfigureLocalAuthAsync(LocalAuthSetupRequest request,
        CancellationToken cancellationToken = default) => setupService.ConfigureLocalAuthAsync(request);

    public Task ConfigureOidcAsync(OidcAuthSetupRequest request, CancellationToken cancellationToken = default) =>
        setupService.ConfigureOidcAsync(request);

    public Task<bool> TestOidcConnectionAsync(string authority, CancellationToken cancellationToken = default) =>
        setupService.TestOidcConnectionAsync(authority);

    public Task FinishSetupAsync(CancellationToken cancellationToken = default) => setupService.FinishSetupAsync();
}