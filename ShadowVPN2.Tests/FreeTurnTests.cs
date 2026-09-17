using System.Text;
using AwesomeAssertions;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class FreeTurnTests {
    [Fact]
    public void Valid_transport_should_reference_protocol_and_have_distinct_ports() {
        var protocol = new WireGuardAmneziaGlobalSettings { Id = Guid.NewGuid() };
        var configuration = new EntityGlobalConfiguration {
            Protocols = [protocol],
            Transports = [
                new FreeTurnTransportSettings {
                    Id = Guid.NewGuid(),
                    ProtocolId = protocol.Id,
                    ListenPort = 56000,
                    ObfuscationKey = new string('a', 64)
                }
            ]
        };

        var action = () => GlobalConfigurationValidator.Validate(configuration);

        action.Should().NotThrow();
    }

    [Fact]
    public void Invalid_transport_reference_should_be_rejected() {
        var configuration = new EntityGlobalConfiguration {
            Protocols = [new WireGuardAmneziaGlobalSettings { Id = Guid.NewGuid() }],
            Transports = [
                new FreeTurnTransportSettings {
                    Id = Guid.NewGuid(),
                    ProtocolId = Guid.NewGuid(),
                    ListenPort = 56000
                }
            ]
        };

        var action = () => GlobalConfigurationValidator.Validate(configuration);

        action.Should().Throw<ArgumentException>().Which.Message.Should().Contain("unknown protocol");
    }

    [Fact]
    public void Null_obfuscation_profile_should_not_require_a_key() {
        var protocol = new WireGuardAmneziaGlobalSettings { Id = Guid.NewGuid() };
        var configuration = new EntityGlobalConfiguration {
            Protocols = [protocol],
            Transports = [
                new FreeTurnTransportSettings {
                    Id = Guid.NewGuid(),
                    ProtocolId = protocol.Id,
                    ListenPort = 56000,
                    ObfuscationProfile = null,
                    ObfuscationKey = null
                }
            ]
        };

        var action = () => GlobalConfigurationValidator.Validate(configuration);

        action.Should().NotThrow();
    }

    [Fact]
    public void FreeTurn_share_url_should_match_upstream_payload() {
        var connection = new FreeTurnConnectionInfo {
            Peer = "vpn.example.com:56000",
            ObfuscationProfile = FreeTurnObfuscationProfile.RtpOpus3,
            ObfuscationKey = new string('a', 64),
            TurnTransport = FreeTurnTurnTransport.Tcp,
            Mode = ProtocolSocketKind.Udp,
            Streams = 12,
            ClientId = "client-id"
        };

        var shareUrl = connection.CreateShareUrl("Laptop");
        var encoded = shareUrl["freeturn://".Length..].Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

        payload.Should().Contain("\"provider\":\"vk\"");
        payload.Should().Contain("\"peer\":\"vpn.example.com:56000\"");
        payload.Should().Contain("\"obf\":\"rtpopus3\"");
        payload.Should().Contain("\"cid\":\"client-id\"");
    }
}