using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Subscriptions;
using ShadowVPN2.Data.Subscription.Links;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ProxyUriBuilderTests {
    [Fact]
    public void Build_should_escape_credentials_query_and_fragment() {
        var uri = new ProxyUriBuilder("proxy", HostName.Parse("example.com"), 443)
            .WithCredentials("user name", "pass@word")
            .AddQuery("name", "client name")
            .WithFragment("fragment value")
            .Build();

        uri.Should().Be("proxy://user%20name:pass%40word@example.com:443/?name=client%20name#fragment%20value");
    }

    [Theory]
    [InlineData("2001:db8::1", "[2001:db8::1]")]
    public void Build_should_bracket_ipv6_host(string host, string formattedHost) {
        var uri = new ProxyUriBuilder("proxy", HostName.Parse(host), 443).Build();

        uri.Should().Be($"proxy://{formattedHost}:443/");
    }

    [Fact]
    public void Build_should_omit_empty_query_parameters() {
        var uri = new ProxyUriBuilder("proxy", HostName.Parse("example.com"), 443)
            .AddQuery("present", "value")
            .AddQuery("null", null)
            .AddQuery("empty", string.Empty)
            .Build();

        uri.Should().Be("proxy://example.com:443/?present=value");
    }

    [Fact]
    public void Build_awg_share_url_should_use_throne_query_format() {
        var connection = new WireGuardConnectionInfo {
            ServerAddress = HostName.Parse("edge.example.com"), ServerPort = 550,
            PrivateKey = "private=", AssignedIp = "100.64.1.1", ServerPublicKey = "public+key",
            Mtu = 1408, IsAmneziaWg = true, Jc = 2, Jmin = 10, Jmax = 50,
            S1 = 34, S2 = 84, S3 = 20, S4 = 23, H1 = "264548", H2 = "859751", H3 = "897465", H4 = "859745"
        };

        WireGuardShareUrlBuilder.Build(connection).Should()
            .Be(
                "wg://edge.example.com:550?private_key=private%3D&local_address=100.64.1.1%2F32&mtu=1408&enable_amnezia=true&public_key=public%2Bkey&jc=2&jmin=10&jmax=50&s1=34&s2=84&s3=20&s4=23&h1=264548&h2=859751&h3=897465&h4=859745&persistent_keepalive_interval=25");
    }
}