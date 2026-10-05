using System.Text;
using AwesomeAssertions;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class SubscriptionFormatServiceTests {
    [Fact]
    public void Base64_contains_only_standard_proxy_links() {
        var response = CreateResponse();
        var encoded = new SubscriptionFormatService().Render(response, SubscriptionFormat.Base64);
        var links = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

        links.Should().Contain("hysteria2://");
        links.Should().NotContain("freeturn://");
    }

    [Fact]
    public void Mihomo_provider_contains_proxy_entries_without_free_turn() {
        var response = CreateResponse();
        var yaml = new SubscriptionFormatService().Render(response, SubscriptionFormat.MihomoProvider);

        yaml.Should().Contain("proxies:");
        yaml.Should().Contain("type: hysteria2");
        yaml.Should().NotContain("freeturn");
        yaml.Should().NotContain("proxy-groups:");
    }

    [Fact]
    public void FreeTurn_format_contains_only_free_turn_links() {
        var response = CreateResponse();
        var content = new SubscriptionFormatService().Render(response, SubscriptionFormat.FreeTurn);

        content.Should().StartWith("freeturn://");
        content.Should().NotContain("hysteria2://");
    }

    private static SubscriptionResponse CreateResponse() {
        return new SubscriptionResponse {
            ClientName = "device",
            Protocols = [
                new ProtocolSubscription {
                    Protocol = "Hysteria2",
                    Endpoints = [
                        new SubscriptionEndpoint {
                            Name = "Main",
                            Address = HostAddress.Parse("vpn.example.com"),
                            IsMain = true,
                            Connection = new Hysteria2ConnectionInfo {
                                ServerAddress = "vpn.example.com",
                                ServerPort = 443,
                                Password = "password",
                                Sni = "vpn.example.com"
                            },
                            Transports = [
                                new FreeTurnConnectionInfo {
                                    Peer = "vpn.example.com:56000",
                                    ObfuscationProfile = null,
                                    ObfuscationKey = null,
                                    TurnTransport = FreeTurnTurnTransport.Tcp,
                                    Mode = ProtocolSocketKind.Udp,
                                    Streams = 4,
                                    ClientId = "client"
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }
}