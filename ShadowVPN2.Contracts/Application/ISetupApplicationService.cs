using ShadowVPN2.Contracts.Setup;

namespace ShadowVPN2.Contracts.Application;

public interface ISetupApplicationService {
    Task<string?> GetPublicIpAsync(CancellationToken cancellationToken = default);
    Task ConfigureNodeAsync(NodeSetupRequest request, CancellationToken cancellationToken = default);
    Task ConfigureLocalAuthAsync(LocalAuthSetupRequest request, CancellationToken cancellationToken = default);
    Task ConfigureOidcAsync(OidcAuthSetupRequest request, CancellationToken cancellationToken = default);
    Task<bool> TestOidcConnectionAsync(string authority, CancellationToken cancellationToken = default);
    Task FinishSetupAsync(CancellationToken cancellationToken = default);
}