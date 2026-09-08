using AwesomeAssertions;
using ShadowVPN2.Data.Subscription;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ProxyUriBuilderTests {
    [Fact]
    public void Build_should_escape_credentials_query_and_fragment() {
        var uri = new ProxyUriBuilder("proxy", "example.com", 443)
            .WithCredentials("user name", "pass@word")
            .AddQuery("name", "client name")
            .WithFragment("fragment value")
            .Build();

        uri.Should().Be("proxy://user%20name:pass%40word@example.com:443/?name=client%20name#fragment%20value");
    }

    [Theory]
    [InlineData("2001:db8::1", "[2001:db8::1]")]
    [InlineData("[2001:db8::1]", "[2001:db8::1]")]
    public void Build_should_bracket_ipv6_host(string host, string formattedHost) {
        var uri = new ProxyUriBuilder("proxy", host, 443).Build();

        uri.Should().Be($"proxy://{formattedHost}:443/");
    }

    [Fact]
    public void Build_should_omit_empty_query_parameters() {
        var uri = new ProxyUriBuilder("proxy", "example.com", 443)
            .AddQuery("present", "value")
            .AddQuery("null", null)
            .AddQuery("empty", string.Empty)
            .Build();

        uri.Should().Be("proxy://example.com:443/?present=value");
    }
}