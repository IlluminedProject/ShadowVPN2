using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Infrastructure.Extensions;
using SubscriptionContract = ShadowVPN2.Contracts.Subscriptions;

namespace ShadowVPN2.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class SubscriptionController(
    SubscriptionService subscriptionService,
    SubscriptionFormatService formatService) : ControllerBase {
    [HttpGet("{id:guid}")]
    public async Task<SubscriptionContract.SubscriptionResponse> GetSubscription(Guid id) {
        var response = await subscriptionService.GetSubscriptionAsync(id);
        response = response.OrThrowNotFound("Subscription not found");

        return new SubscriptionContract.SubscriptionResponse {
            ClientName = response.ClientName,
            Protocols = response.Protocols.Select(protocol => new SubscriptionContract.ProtocolSubscription {
                Protocol = protocol.Protocol,
                Endpoints = protocol.Endpoints.Select(endpoint => new SubscriptionContract.SubscriptionEndpoint {
                    Name = endpoint.Name,
                    Address = endpoint.Address.ToString(),
                    IsMain = endpoint.IsMain,
                    Connection = MapConnection(endpoint.Connection),
                    Transports = endpoint.Transports.Select(MapTransport).ToArray()
                }).ToArray()
            }).ToArray()
        };
    }

    [HttpGet("~/s/{id:guid}/subscription")]
    public async Task<ContentResult> GetSubscriptionContent(
        Guid id,
        [FromQuery] SubscriptionFormat? format = null) {
        var response = await subscriptionService.GetSubscriptionAsync(id);
        response = response.OrThrowNotFound("Subscription not found");

        var selectedFormat = format ?? SelectFormat(Request.Headers.UserAgent.ToString());
        if (selectedFormat == SubscriptionFormat.Json) {
            throw new ArgumentException("The json format is available at the API subscription endpoint",
                nameof(format));
        }

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Vary = "User-Agent";
        return new ContentResult {
            Content = formatService.Render(response, selectedFormat),
            ContentType = selectedFormat switch {
                SubscriptionFormat.Base64 or SubscriptionFormat.FreeTurn => "text/plain; charset=utf-8",
                _ => "application/yaml; charset=utf-8"
            }
        };
    }

    private static SubscriptionFormat SelectFormat(string userAgent) {
        return userAgent.Contains("mihomo", StringComparison.OrdinalIgnoreCase) ||
               userAgent.Contains("clash", StringComparison.OrdinalIgnoreCase)
            ? SubscriptionFormat.Mihomo
            : SubscriptionFormat.Base64;
    }

    private static SubscriptionContract.ProtocolConnectionInfo MapConnection(ProtocolConnectionInfo connection) =>
        connection switch {
            WireGuardConnectionInfo wireGuard => new SubscriptionContract.WireGuardConnectionInfo {
                ServerAddress = wireGuard.ServerAddress,
                ServerPort = wireGuard.ServerPort,
                PrivateKey = wireGuard.PrivateKey,
                AssignedIp = wireGuard.AssignedIp,
                ServerPublicKey = wireGuard.ServerPublicKey,
                Mtu = wireGuard.Mtu,
                IsAmneziaWg = wireGuard.IsAmneziaWg,
                Jc = wireGuard.Jc,
                Jmin = wireGuard.Jmin,
                Jmax = wireGuard.Jmax,
                S1 = wireGuard.S1,
                S2 = wireGuard.S2,
                S3 = wireGuard.S3,
                S4 = wireGuard.S4,
                H1 = wireGuard.H1,
                H2 = wireGuard.H2,
                H3 = wireGuard.H3,
                H4 = wireGuard.H4,
                I1 = wireGuard.I1,
                I2 = wireGuard.I2,
                I3 = wireGuard.I3,
                I4 = wireGuard.I4,
                I5 = wireGuard.I5
            },
            Hysteria2ConnectionInfo hysteria2 => new SubscriptionContract.Hysteria2ConnectionInfo {
                ServerAddress = hysteria2.ServerAddress,
                ServerPort = hysteria2.ServerPort,
                Password = hysteria2.Password,
                ObfsType = hysteria2.ObfsType,
                ObfsPassword = hysteria2.ObfsPassword,
                Sni = hysteria2.Sni,
                PinSha256 = hysteria2.PinSha256
            },
            NaiveProxyConnectionInfo naiveProxy => new SubscriptionContract.NaiveProxyConnectionInfo {
                ServerAddress = naiveProxy.ServerAddress,
                ServerPort = naiveProxy.ServerPort,
                Username = naiveProxy.Username,
                Password = naiveProxy.Password,
                Sni = naiveProxy.Sni,
                Insecure = naiveProxy.Insecure,
                PinSha256 = naiveProxy.PinSha256
            },
            _ => throw new ArgumentOutOfRangeException(nameof(connection), connection, null)
        };

    private static SubscriptionContract.TransportConnectionInfo MapTransport(TransportConnectionInfo transport) =>
        transport switch {
            FreeTurnConnectionInfo freeTurn => new SubscriptionContract.FreeTurnConnectionInfo {
                Peer = freeTurn.Peer,
                ObfuscationProfile = freeTurn.ObfuscationProfile?.ToString(),
                ObfuscationKey = freeTurn.ObfuscationKey,
                TurnTransport = freeTurn.TurnTransport.ToString(),
                Mode = (int)freeTurn.Mode,
                Streams = freeTurn.Streams,
                ClientId = freeTurn.ClientId
            },
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
        };
}