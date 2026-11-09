using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Frontend.Api;
using Xunit;

namespace ShadowVPN2.Frontend.Tests;

public sealed class NaiveProxyShareUrlTests {
    [Fact]
    public void Naive_share_url_includes_insecure_and_certificate_pin_when_domain_is_absent() {
        var connection = new NaiveProxyConnectionInfo {
            ServerAddress = HostName.Parse("203.0.113.10"),
            ServerPort = 443,
            Username = "user",
            Password = "pass word",
            Sni = HostName.Parse("shadowvpn.local"),
            Insecure = true,
            PinSha256 = "AA:BB"
        };

        var link = SubscriptionLinkBuilder.CreateNaiveProxyShareUrl(connection, "my device");

        link.Should().Be("naive+https://user:pass%20word@203.0.113.10:443?sni=shadowvpn.local" +
                         "&insecure=1&pinSHA256=AA%3ABB&insecure-concurrency=2&uot=1&udp_over_tcp=1&name=my%20device");
    }
}