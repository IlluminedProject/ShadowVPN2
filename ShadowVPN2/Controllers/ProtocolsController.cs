using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;
using Contract = ShadowVPN2.Contracts.Protocols;
using UpdateProtocolsSettingsRequest = ShadowVPN2.Data.Protocols.UpdateProtocolsSettingsRequest;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AppPermissions.Settings.View)]
public class ProtocolsController(
    ProtocolSettingsService protocolSettingsService,
    GlobalConfigurationService globalConfigurationService) : ControllerBase {
    [HttpGet]
    public async Task<Contract.ProtocolsSettingsResponse> Get() {
        var settings = await protocolSettingsService.GetSettingsAsync();
        return new Contract.ProtocolsSettingsResponse {
            MainDomain = settings.MainDomain,
            Protocols = settings.Protocols.Select(MapProtocol).ToList(),
            Transports = settings.Transports.Select(MapTransport).ToList()
        };
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public async Task Save([FromBody] Contract.UpdateProtocolsSettingsRequest request) {
        var existing = await globalConfigurationService.GetAsync();
        var protocols = request.Protocols.Select(item => MapProtocol(item,
            existing.Protocols.FirstOrDefault(current => current.Id == item.Id))).ToList();
        await protocolSettingsService.UpdateSettingsAsync(new UpdateProtocolsSettingsRequest {
            MainDomain = request.MainDomain,
            Protocols = protocols,
            Transports = request.Transports.Select(MapTransport).ToList()
        });
    }

    [HttpPost("{protocolId:guid}/regenerate-certificate")]
    [Authorize(Policy = AppPermissions.Settings.Manage)]
    public Task RegenerateCertificate(Guid protocolId, CancellationToken cancellationToken) {
        return protocolSettingsService.RegenerateCertificateAsync(protocolId, cancellationToken);
    }

    private static ProtocolSettingsDto MapProtocol(ProtocolGlobalSettings settings) => settings switch {
        Hysteria2GlobalSettings hysteria2 => new Hysteria2ProtocolSettingsDto {
            Id = hysteria2.Id,
            ListenPort = hysteria2.ListenPort,
            Enabled = hysteria2.Enabled,
            MainDomain = hysteria2.MainDomain,
            ObfsType = hysteria2.ObfsType,
            ObfsPassword = hysteria2.ObfsPassword,
            HasTlsCertificate = !string.IsNullOrEmpty(hysteria2.TlsCertificatePem) &&
                                !string.IsNullOrEmpty(hysteria2.TlsKeyPem)
        },
        AwgGlobalSettings awg => new AwgProtocolSettingsDto {
            Id = awg.Id,
            ListenPort = awg.ListenPort,
            Enabled = awg.Enabled,
            MainDomain = awg.MainDomain,
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
        },
        NaiveProxyGlobalSettings naiveProxy => new NaiveProxyProtocolSettingsDto {
            Id = naiveProxy.Id,
            ListenPort = naiveProxy.ListenPort,
            Enabled = naiveProxy.Enabled,
            MainDomain = naiveProxy.MainDomain,
            HasTlsCertificate = !string.IsNullOrEmpty(naiveProxy.TlsCertificatePem) &&
                                !string.IsNullOrEmpty(naiveProxy.TlsKeyPem)
        },
        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings, null)
    };

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
            case AwgProtocolSettingsDto awg: {
                var current = existing as AwgGlobalSettings;
                return new AwgGlobalSettings {
                    Id = awg.Id,
                    ListenPort = awg.ListenPort,
                    Enabled = awg.Enabled,
                    MainDomain = awg.MainDomain,
                    PrivateKey = current?.PrivateKey ?? new AwgGlobalSettings().PrivateKey,
                    ServerAddress = current?.ServerAddress ?? "100.64.0.1/10",
                    Mtu = current?.Mtu ?? 1408,
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
            }
            case NaiveProxyProtocolSettingsDto naiveProxy: {
                var current = existing as NaiveProxyGlobalSettings;
                var result = new NaiveProxyGlobalSettings {
                    Id = naiveProxy.Id,
                    ListenPort = naiveProxy.ListenPort,
                    Enabled = naiveProxy.Enabled,
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

    private static TransportSettingsDto MapTransport(TransportSettings transport) => transport switch {
        FreeTurnTransportSettings freeTurn => new FreeTurnTransportSettingsDto {
            Id = freeTurn.Id,
            ProtocolId = freeTurn.ProtocolId,
            Enabled = freeTurn.Enabled,
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

    private static TransportSettings MapTransport(TransportSettingsDto transport) => transport switch {
        FreeTurnTransportSettingsDto freeTurn => new FreeTurnTransportSettings {
            Id = freeTurn.Id,
            ProtocolId = freeTurn.ProtocolId,
            Enabled = freeTurn.Enabled,
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