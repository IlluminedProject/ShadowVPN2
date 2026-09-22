using AwesomeAssertions;
using ShadowVPN2.Data;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class HostAddressTests {
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("example.com:443", "example.com")]
    [InlineData("https://example.com:443/path", "example.com")]
    [InlineData("[2001:db8::1]:443", "2001:db8::1")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    public void Parse_should_extract_host(string address, string expected) {
        HostAddress.Parse(address).Value.Should().Be(expected);
    }

    [Fact]
    public void Parse_should_preserve_non_default_port() {
        var address = HostAddress.Parse("https://example.com:8443/path");

        address.Port.Should().Be(8443);
        address.FormatWithPort().Should().Be("example.com:8443");
    }

    [Fact]
    public void Parse_should_preserve_explicit_default_port() {
        var address = HostAddress.Parse("https://example.com:443");

        address.Port.Should().Be(443);
        address.ToString().Should().Be("example.com");
        ((string)address).Should().Be("example.com");
    }

    [Fact]
    public void ToString_should_include_non_default_port() {
        HostAddress.Parse("example.com:8443").ToString().Should().Be("example.com:8443");
    }

    [Theory]
    [InlineData("2001:db8::1", "[2001:db8::1]:443")]
    [InlineData("[2001:db8::1]", "[2001:db8::1]:443")]
    [InlineData("example.com", "example.com:443")]
    public void FormatWithPort_should_format_ipv6_authority(string host, string expected) {
        HostAddress.Parse(host).FormatWithPort(443).Should().Be(expected);
    }
}