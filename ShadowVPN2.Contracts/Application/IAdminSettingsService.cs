using ShadowVPN2.Contracts.Auth;
using ShadowVPN2.Contracts.Certificates;
using ShadowVPN2.Contracts.Cluster;
using ShadowVPN2.Contracts.Protocols;

namespace ShadowVPN2.Contracts.Application;

public interface IAdminSettingsService {
    Task<AuthSettingsResponse> GetAuthSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveAuthSettingsAsync(UpdateAuthSettingsRequest request, CancellationToken cancellationToken = default);
    Task<bool> TestOidcAsync(string authority, CancellationToken cancellationToken = default);
    Task<AcmeSettingsResponse> GetCertificateSettingsAsync(CancellationToken cancellationToken = default);
    Task<ManagedCertificateResponse?> GetLocalCertificateAsync(CancellationToken cancellationToken = default);

    Task SaveCertificateSettingsAsync(UpdateAcmeSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task RenewLocalCertificateAsync(CancellationToken cancellationToken = default);
    Task<ClusterSettingsResponse> GetClusterSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveClusterSettingsAsync(UpdateClusterSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<AwgClusterSettings> GetAwgSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveAwgSettingsAsync(AwgClusterSettings request, CancellationToken cancellationToken = default);
    Task<ProtocolsSettingsResponse> GetProtocolsAsync(CancellationToken cancellationToken = default);
    Task SaveProtocolsAsync(UpdateProtocolsSettingsRequest request, CancellationToken cancellationToken = default);
    Task RegenerateProtocolCertificateAsync(Guid protocolId, CancellationToken cancellationToken = default);
}