using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
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

    [Theory]
    [InlineData(false, "naive+https")]
    [InlineData(true, "naive+quic")]
    public void Base64_naive_links_preserve_transport_credentials_and_tls_options(bool quic, string scheme) {
        var response = CreateResponse();
        response.Protocols[0].Endpoints[0].Connection = new NaiveProxyConnectionInfo {
            ServerAddress = HostName.Parse("2001:db8::1"),
            ServerPort = 8443,
            Quic = quic,
            Username = "user name",
            Password = "pass&word",
            Sni = HostName.Parse("shadowvpn.local"),
            Insecure = true,
            PinSha256 = "AA:BB"
        };

        var encoded = new SubscriptionFormatService().Render(response, SubscriptionFormat.Base64);
        var link = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

        var uri = new Uri(link);
        uri.Scheme.Should().Be(scheme);
        uri.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Should().Be("user name:pass&word");
        var query = QueryHelpers.ParseQuery(uri.Query);
        query["sni"].ToString().Should().Be("shadowvpn.local");
        query["insecure"].ToString().Should().Be("1");
        query["pinSHA256"].ToString().Should().Be("AA:BB");
        query["insecure-concurrency"].ToString().Should().Be("2");
        query["uot"].ToString().Should().Be("1");
        query["udp_over_tcp"].ToString().Should().Be("1");
        query["name"].ToString().Should().Be("device");
    }

    [Theory]
    [InlineData(SubscriptionFormat.Mihomo)]
    [InlineData(SubscriptionFormat.MihomoProvider)]
    public void Mihomo_rejects_naive_quic_instead_of_exporting_an_https_connection(SubscriptionFormat format) {
        var response = CreateResponse();
        response.Protocols[0].Endpoints[0].Connection = new NaiveProxyConnectionInfo {
            ServerAddress = HostName.Parse("vpn.example.com"),
            ServerPort = 8443,
            Quic = true,
            Username = "user",
            Password = "password",
            Sni = HostName.Parse("vpn.example.com")
        };

        var render = () => new SubscriptionFormatService().Render(response, format);

        render.Should().Throw<ArgumentException>().WithMessage("*NaiveProxy QUIC*Base64 or JSON*");
    }

    [Theory]
    [InlineData(SubscriptionFormat.Base64)]
    [InlineData(SubscriptionFormat.Mihomo)]
    [InlineData(SubscriptionFormat.MihomoProvider)]
    [InlineData(SubscriptionFormat.FreeTurn)]
    public void Formats_keep_protocols_for_each_node_together(SubscriptionFormat format) {
        SubscriptionEndpoint Endpoint(string name, string host, bool main = false) {
            var endpoint = CreateResponse().Protocols[0].Endpoints[0];
            endpoint.Name = name;
            endpoint.IsMain = main;
            endpoint.Address = HostAddress.Parse(host);
            ((Hysteria2ConnectionInfo)endpoint.Connection).ServerAddress = HostName.Parse(host);
            endpoint.Transports = [
                new FreeTurnConnectionInfo {
                    Peer = HostAddress.Parse(host + ":56000"),
                    ObfuscationProfile = null,
                    ObfuscationKey = null,
                    TurnTransport = FreeTurnTurnTransport.Tcp,
                    Mode = ProtocolSocketKind.Udp,
                    Streams = 4,
                    ClientId = "client"
                }
            ];
            return endpoint;
        }

        var response = new SubscriptionResponse {
            ClientName = "device",
            Protocols = [
                new ProtocolSubscription {
                    Protocol = "first",
                    Endpoints = [
                        Endpoint("Zulu", "z.example"), Endpoint("Alpha", "a.example"),
                        Endpoint("Main", "main.example", true)
                    ]
                },
                new ProtocolSubscription {
                    Protocol = "second",
                    Endpoints = [
                        Endpoint("Zulu", "z.example"), Endpoint("Alpha", "a.example"),
                        Endpoint("Main", "main.example", true)
                    ]
                }
            ]
        };
        var content = new SubscriptionFormatService().Render(response, format);
        if (format == SubscriptionFormat.Base64)
            content = Encoding.UTF8.GetString(Convert.FromBase64String(content));

        var hosts = content.Split('\n')
            .Where(line => format is SubscriptionFormat.Mihomo or SubscriptionFormat.MihomoProvider
                ? line.TrimStart().StartsWith("server:")
                : line.Length > 0)
            .Select(line => format == SubscriptionFormat.FreeTurn
                ? Encoding.UTF8.GetString(Convert.FromBase64String(
                    line["freeturn://".Length..].Replace('-', '+').Replace('_', '/').PadRight(
                        (line.Length - "freeturn://".Length + 3) / 4 * 4, '=')))
                : line)
            .Select(line => new[] { "main.example", "a.example", "z.example" }.Single(line.Contains));
        hosts.Should().Equal("main.example", "main.example", "a.example", "a.example", "z.example", "z.example");
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