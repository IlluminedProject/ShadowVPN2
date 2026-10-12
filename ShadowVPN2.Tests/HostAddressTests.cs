using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class HostAddressTests {
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("example.com:443", "example.com")]
    [InlineData("[2001:db8::1]:443", "2001:db8::1")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    public void Parse_should_extract_host(string address, string expected) {
        HostAddress.Parse(address).Host.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(" Example.COM. ", "example.com")]
    [InlineData("пример.рф", "xn--e1afmkfd.xn--p1ai")]
    [InlineData(" Example.COM.:443 ", "example.com")]
    [InlineData("example.com:08443", "example.com:8443")]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData("203.0.113.10:8443", "203.0.113.10:8443")]
    [InlineData("[2001:DB8::1]:443", "[2001:db8::1]")]
    [InlineData("[2001:db8::1]:8443", "[2001:db8::1]:8443")]
    [InlineData("example.com:1", "example.com:1")]
    [InlineData("example.com:65535", "example.com:65535")]
    [InlineData("[2001:db8::1]", "[2001:db8::1]")]
    public void Parse_should_return_canonical_address(string value, string expected) {
        HostAddress.Parse(value).ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("https://example.com:443/path")]
    [InlineData("example.com/path")]
    [InlineData("example.com?query")]
    [InlineData("example.com#fragment")]
    [InlineData("user@example.com")]
    [InlineData("invalid host")]
    [InlineData(":443")]
    [InlineData("example.com:")]
    [InlineData("example.com:0")]
    [InlineData("example.com:65536")]
    [InlineData("example.com:+443")]
    [InlineData("example.com: 443")]
    [InlineData("example.com:abc")]
    [InlineData("[2001:db8::1")]
    [InlineData("[2001:db8::1]suffix")]
    [InlineData("[2001:db8::1]:")]
    [InlineData("[203.0.113.10]:443")]
    [InlineData("2001:db8::1:65536")]
    public void Parse_should_reject_invalid_addresses(string? value) {
        var action = () => HostAddress.Parse(value!);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Json_should_round_trip_canonical_address() {
        var address = JsonSerializer.Deserialize<HostAddress>("\" Example.COM.:443 \"");

        address.Host.Value.Should().Be("example.com");
        JsonSerializer.Serialize(address).Should().Be("\"example.com\"");
    }

    [Fact]
    public void Parse_should_preserve_non_default_port() {
        var address = HostAddress.Parse("example.com:8443");

        address.Port.Should().Be(8443);
        address.FormatWithPort().Should().Be("example.com:8443");
    }

    [Fact]
    public void Parse_should_preserve_explicit_default_port() {
        var address = HostAddress.Parse("example.com:443");

        address.Port.Should().Be(443);
        address.ToString().Should().Be("example.com");
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