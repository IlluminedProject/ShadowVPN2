using System.Text;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
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

    [Fact]
    public void IPv6_hosts_are_bracketed_in_links_and_unbracketed_in_mihomo_server() {
        var response = CreateResponse();
        var endpoint = response.Protocols[0].Endpoints[0];
        var hysteria2 = (Hysteria2ConnectionInfo)endpoint.Connection;
        hysteria2.ServerAddress = HostName.Parse("2001:db8::1");
        var service = new SubscriptionFormatService();

        var hysteriaLinks = Encoding.UTF8.GetString(Convert.FromBase64String(
            service.Render(response, SubscriptionFormat.Base64)));
        var yaml = service.Render(response, SubscriptionFormat.MihomoProvider);

        hysteriaLinks.Should().Contain("@[2001:db8::1]:443/");
        yaml.Should().Contain("server: \"2001:db8::1\"");
        hysteria2.PortBinding = new PortBinding(20000, 30000);
        var rangedLinks = Encoding.UTF8.GetString(Convert.FromBase64String(
            service.Render(response, SubscriptionFormat.Base64)));
        rangedLinks.Should().Contain("mport=20000-30000");
        rangedLinks.Should().Contain("@[2001:db8::1]:20000/");
        var rangedYaml = service.Render(response, SubscriptionFormat.MihomoProvider);
        rangedYaml.Should().Contain("ports: \"20000-30000\"");
        rangedYaml.Should().Contain("port: 20000");

        endpoint.Connection = new NaiveProxyConnectionInfo {
            ServerAddress = HostName.Parse("2001:db8::1"),
            ServerPort = 443,
            Username = "user",
            Password = "password",
            Sni = HostName.Parse("vpn.example.com")
        };
        var naiveLinks = Encoding.UTF8.GetString(Convert.FromBase64String(
            service.Render(response, SubscriptionFormat.Base64)));

        naiveLinks.Should().Contain("naive+https://user:password@[2001:db8::1]:443/?sni=vpn.example.com");
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
                                ServerAddress = HostName.Parse("vpn.example.com"),
                                PortBinding = new PortBinding(443),
                                Password = "password",
                            },
                            Transports = [
                                new FreeTurnConnectionInfo {
                                    Peer = HostAddress.Parse("vpn.example.com:56000"),
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