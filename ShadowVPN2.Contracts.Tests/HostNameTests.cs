using System.ComponentModel;
using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Nodes;
using Xunit;

namespace ShadowVPN2.Contracts.Tests;

public sealed class HostNameTests {
    [Theory]
    [InlineData(" Example.COM. ", "example.com")]
    [InlineData("пример.рф", "xn--e1afmkfd.xn--p1ai")]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData("2001:DB8::1", "2001:db8::1")]
    public void Parse_should_canonicalize_host_without_port(string value, string expected) {
        HostName.Parse(value).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("example.com:443")]
    [InlineData("203.0.113.10:8443")]
    [InlineData("[2001:db8::1]:443")]
    [InlineData("[2001:db8::1]")]
    [InlineData("https://example.com/path")]
    [InlineData("user@example.com")]
    public void Parse_should_reject_addresses_and_urls(string value) {
        var action = () => HostName.Parse(value);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Address_should_keep_port_separate_from_tls_host() {
        var address = HostAddress.Parse(" Example.COM.:8443 ");
        address.Host.Should().Be(HostName.Parse("example.com"));
        address.Host.ToString().Should().Be("example.com");
        address.ToString().Should().Be("example.com:8443");
    }


    [Fact]
    public void Type_converter_should_preserve_string_storage_contract() {
        var converter = TypeDescriptor.GetConverter(typeof(HostName));
        var host = (HostName)converter.ConvertFromInvariantString(" Example.COM. ")!;

        host.Should().Be(HostName.Parse("example.com"));
        converter.ConvertToInvariantString(host).Should().Be("example.com");
    }

    [Fact]
    public void Domain_request_should_reject_port_as_json_validation_error() {
        var action = () => JsonSerializer.Deserialize<UpdateNodeRequest>(
            "{\"Domain\":\"example.com:8443\"}");

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void Domain_request_should_preserve_typed_canonical_hostname() {
        var request = JsonSerializer.Deserialize<UpdateNodeRequest>(
            "{\"Domain\":\" Example.COM. \"}")!;

        request.Domain.Should().Be(HostName.Parse("example.com"));
    }
}