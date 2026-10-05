using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Certificates;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Infrastructure.Authentication;
using ShadowVPN2.Infrastructure.Configurations;
using Contract = ShadowVPN2.Contracts.Protocols;
using UpdateProtocolsSettingsRequest = ShadowVPN2.Data.Protocols.UpdateProtocolsSettingsRequest;

namespace ShadowVPN2.Frontend;

public sealed class AdminSettingsService(
    SettingsService settingsService,
    CertificateSettingsService certificateSettingsService,
    ManagedCertificateService managedCertificateService,
    AcmeCertificateService acmeCertificateService,
    ClusterSettingsService clusterSettingsService,
    ProtocolSettingsService protocolSettingsService,
    GlobalConfigurationService globalConfigurationService,
    IOptions<LocalConfiguration> localConfiguration,
    FrontendUserContext userContext) : IAdminSettingsService {
    public async Task<AuthSettingsResponse> GetAuthSettingsAsync(CancellationToken cancellationToken = default) {
        await RequireViewAsync(cancellationToken);
        var config = await settingsService.GetConfigurationAsync();
        var oidc = config.Providers.OfType<OidcAuthProvider>().FirstOrDefault();
        var local = config.Providers.OfType<LocalAuthProvider>().FirstOrDefault();
        return new AuthSettingsResponse {
            EnableLocalLogin = local?.IsEnabled == true,
            SelfRegistrationEnabled = config.SelfRegistrationEnabled,
            EnableOidc = oidc?.IsEnabled == true,
            OidcSettings = oidc is null
                ? null
                : new OidcAuthSettings {
                    DisplayName = oidc.DisplayName,
                    Authority = oidc.Authority,
                    ClientId = oidc.ClientId,
                    ClientSecret = string.Empty,
                    DeviceFlowEnabled = oidc.DeviceFlowEnabled,
                    Scopes = oidc.Scopes
                }
        };
    }

    public async Task SaveAuthSettingsAsync(UpdateAuthSettingsRequest request,
        CancellationToken cancellationToken = default) {
        await RequireManageAsync(cancellationToken);
        await settingsService.SaveAuthSettingsAsync(request);
    }

    public async Task<bool> TestOidcAsync(string authority, CancellationToken cancellationToken = default) {
        await RequireManageAsync(cancellationToken);
        return await settingsService.TestOidcConnectionAsync(authority);
    }

    public async Task<AcmeSettingsResponse> GetCertificateSettingsAsync(
        CancellationToken cancellationToken = default) {
        await RequireViewAsync(cancellationToken);
        var settings = await certificateSettingsService.GetAsync(cancellationToken);
        return new AcmeSettingsResponse {
            Enabled = settings.Enabled,
            Email = settings.Email,
            UseStaging = settings.UseStaging,
            HttpPort = settings.HttpPort,
            RenewalThresholdDays = settings.RenewalThresholdDays
        };
    }

    public async Task<ManagedCertificateResponse?> GetLocalCertificateAsync(
        CancellationToken cancellationToken = default) {
        await RequireViewAsync(cancellationToken);
        var certificate = await managedCertificateService.GetEntityAsync(localConfiguration.Value.NodeId,
            cancellationToken);
        return certificate is null
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

    public async Task SaveCertificateSettingsAsync(UpdateAcmeSettingsRequest request,
        CancellationToken cancellationToken = default) {
        await RequireManageAsync(cancellationToken);
        await certificateSettingsService.UpdateAsync(request, cancellationToken);
    }

    public async Task RenewLocalCertificateAsync(CancellationToken cancellationToken = default) {
        await RequireManageAsync(cancellationToken);
        await acmeCertificateService.RenewNowAsync(cancellationToken);
    }

    public async Task<ClusterSettingsResponse> GetClusterSettingsAsync(
        CancellationToken cancellationToken = default) {
        await RequireViewAsync(cancellationToken);
        var settings = await clusterSettingsService.GetSettingsAsync(cancellationToken);
        return new ClusterSettingsResponse { GlobalDomain = settings.GlobalDomain };
    }

    public async Task SaveClusterSettingsAsync(UpdateClusterSettingsRequest request,
        CancellationToken cancellationToken = default) {
        await RequireManageAsync(cancellationToken);
        await clusterSettingsService.UpdateSettingsAsync(request, cancellationToken);
    }

    public async Task<AwgClusterSettings> GetAwgSettingsAsync(CancellationToken cancellationToken = default) {
        await RequireViewAsync(cancellationToken);
        return MapAwgSettings((await globalConfigurationService.GetAsync(cancellationToken)).AwgSettings);
    }

    public async Task SaveAwgSettingsAsync(AwgClusterSettings request,
        CancellationToken cancellationToken = default) {
        await RequireManageAsync(cancellationToken);
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

    public async Task<Contract.ProtocolsSettingsResponse> GetProtocolsAsync(
        CancellationToken cancellationToken = default) {
        await RequireViewAsync(cancellationToken);
        var settings = await protocolSettingsService.GetSettingsAsync();
        return new Contract.ProtocolsSettingsResponse {
            MainDomain = settings.MainDomain,
            Protocols = settings.Protocols.Select(MapProtocol).ToList(),
            Transports = settings.Transports.Select(MapTransport).ToList()
        };
    }

    public async Task SaveProtocolsAsync(Contract.UpdateProtocolsSettingsRequest request,
        CancellationToken cancellationToken = default) {
        await RequireManageAsync(cancellationToken);
        var existing = await globalConfigurationService.GetAsync(cancellationToken);
        var protocols = request.Protocols.Select(item => MapProtocol(item,
            existing.Protocols.FirstOrDefault(current => current.Id == item.Id))).ToList();
        await protocolSettingsService.UpdateSettingsAsync(new UpdateProtocolsSettingsRequest {
            MainDomain = request.MainDomain,
            Protocols = protocols,
            Transports = request.Transports.Select(MapTransport).ToList()
        });
    }

    public async Task RegenerateProtocolCertificateAsync(Guid protocolId,
        CancellationToken cancellationToken = default) {
        await RequireManageAsync(cancellationToken);
        await protocolSettingsService.RegenerateCertificateAsync(protocolId, cancellationToken);
    }

    private Task RequireViewAsync(CancellationToken cancellationToken) {
        return userContext.RequirePolicyAsync(AppPermissions.Settings.View, cancellationToken);
    }

    private Task RequireManageAsync(CancellationToken cancellationToken) {
        return userContext.RequirePolicyAsync(AppPermissions.Settings.Manage, cancellationToken);
    }

    private static AwgClusterSettings MapAwgSettings(AwgGlobalSettings awg) {
        return new AwgClusterSettings {
            ListenPort = awg.ListenPort, H1 = awg.H1, H2 = awg.H2, H3 = awg.H3, H4 = awg.H4,
            Jc = awg.Jc, Jmin = awg.Jmin, Jmax = awg.Jmax, S1 = awg.S1, S2 = awg.S2, S3 = awg.S3,
            S4 = awg.S4, I1 = awg.I1, I2 = awg.I2, I3 = awg.I3, I4 = awg.I4, I5 = awg.I5
        };
    }

    private static ProtocolSettingsDto MapProtocol(ProtocolGlobalSettings settings) {
        return settings switch {
            Hysteria2GlobalSettings hysteria2 => new Hysteria2ProtocolSettingsDto {
                Id = hysteria2.Id, ListenPort = hysteria2.ListenPort, Enabled = hysteria2.Enabled,
                MainDomain = hysteria2.MainDomain, ObfsType = hysteria2.ObfsType,
                ObfsPassword = hysteria2.ObfsPassword,
                HasTlsCertificate = !string.IsNullOrEmpty(hysteria2.TlsCertificatePem) &&
                                    !string.IsNullOrEmpty(hysteria2.TlsKeyPem)
            },
            AwgGlobalSettings awg => new AwgProtocolSettingsDto {
                Id = awg.Id, ListenPort = awg.ListenPort, Enabled = awg.Enabled, MainDomain = awg.MainDomain,
                H1 = awg.H1, H2 = awg.H2, H3 = awg.H3, H4 = awg.H4, Jc = awg.Jc, Jmin = awg.Jmin,
                Jmax = awg.Jmax, S1 = awg.S1, S2 = awg.S2, S3 = awg.S3, S4 = awg.S4, I1 = awg.I1,
                I2 = awg.I2, I3 = awg.I3, I4 = awg.I4, I5 = awg.I5
            },
            NaiveProxyGlobalSettings naiveProxy => new NaiveProxyProtocolSettingsDto {
                Id = naiveProxy.Id, ListenPort = naiveProxy.ListenPort, Enabled = naiveProxy.Enabled,
                MainDomain = naiveProxy.MainDomain,
                HasTlsCertificate = !string.IsNullOrEmpty(naiveProxy.TlsCertificatePem) &&
                                    !string.IsNullOrEmpty(naiveProxy.TlsKeyPem)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings, null)
        };
    }

    private static TransportSettingsDto MapTransport(TransportSettings transport) {
        return transport switch {
            FreeTurnTransportSettings freeTurn => new FreeTurnTransportSettingsDto {
                Id = freeTurn.Id, ProtocolId = freeTurn.ProtocolId, Enabled = freeTurn.Enabled,
                ListenPort = freeTurn.ListenPort,
                ObfuscationProfile = freeTurn.ObfuscationProfile is null
                    ? null
                    : Enum.Parse<FreeTurnObfuscationProfileDto>(freeTurn.ObfuscationProfile.Value.ToString()),
                ObfuscationKey = freeTurn.ObfuscationKey,
                TurnTransport = Enum.Parse<FreeTurnTurnTransportDto>(freeTurn.TurnTransport.ToString()),
                Streams = freeTurn.Streams
            },
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
        };
    }

    private static ProtocolGlobalSettings MapProtocol(ProtocolSettingsDto settings,
        ProtocolGlobalSettings? existing) {
        switch (settings) {
            case Hysteria2ProtocolSettingsDto hysteria2: {
                var current = existing as Hysteria2GlobalSettings;
                var result = new Hysteria2GlobalSettings {
                    Id = hysteria2.Id,
                    ListenPort = hysteria2.ListenPort,
                    Enabled = hysteria2.Enabled,
                    MainDomain = hysteria2.MainDomain,
                    ObfsType = hysteria2.ObfsType,
                    ObfsPassword = hysteria2.ObfsPassword,
                    TlsCertificatePem = current?.TlsCertificatePem ?? string.Empty,
                    TlsKeyPem = current?.TlsKeyPem ?? string.Empty
                };
                if (current is null)
                    result.GenerateSelfSignedCertificate();
                return result;
            }
            case AwgProtocolSettingsDto awg:
                return new AwgGlobalSettings {
                    Id = awg.Id,
                    ListenPort = awg.ListenPort,
                    Enabled = awg.Enabled,
                    MainDomain = awg.MainDomain,
                    PrivateKey = (existing as AwgGlobalSettings)?.PrivateKey ?? new AwgGlobalSettings().PrivateKey,
                    ServerAddress = (existing as AwgGlobalSettings)?.ServerAddress ?? "100.64.0.1/10",
                    Mtu = (existing as AwgGlobalSettings)?.Mtu ?? 1408,
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
            case NaiveProxyProtocolSettingsDto naiveProxy: {
                var current = existing as NaiveProxyGlobalSettings;
                var result = new NaiveProxyGlobalSettings {
                    Id = naiveProxy.Id, ListenPort = naiveProxy.ListenPort, Enabled = naiveProxy.Enabled,
                    MainDomain = naiveProxy.MainDomain,
                    TlsCertificatePem = current?.TlsCertificatePem ?? string.Empty,
                    TlsKeyPem = current?.TlsKeyPem ?? string.Empty
                };
                if (current is null || string.IsNullOrWhiteSpace(result.TlsCertificatePem) ||
                    string.IsNullOrWhiteSpace(result.TlsKeyPem))
                    result.GenerateSelfSignedCertificate();
                return result;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings, null);
        }
    }

    private static TransportSettings MapTransport(TransportSettingsDto transport) {
        return transport switch {
            FreeTurnTransportSettingsDto freeTurn => new FreeTurnTransportSettings {
                Id = freeTurn.Id, ProtocolId = freeTurn.ProtocolId, Enabled = freeTurn.Enabled,
                ListenPort = freeTurn.ListenPort,
                ObfuscationProfile = freeTurn.ObfuscationProfile is null
                    ? null
                    : Enum.Parse<FreeTurnObfuscationProfile>(freeTurn.ObfuscationProfile.Value.ToString()),
                ObfuscationKey = freeTurn.ObfuscationKey,
                TurnTransport = Enum.Parse<FreeTurnTurnTransport>(freeTurn.TurnTransport.ToString()),
                Streams = freeTurn.Streams
            },
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
        };
    }
}