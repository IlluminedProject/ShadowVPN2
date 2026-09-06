using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShadowVPN2.Data;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class DomainValidationServiceTests {
    private readonly DomainValidationService _service = new(NullLogger<DomainValidationService>.Instance);

    [Theory]
    [InlineData(" Example.COM. ", "example.com")]
    [InlineData("пример.рф", "xn--e1afmkfd.xn--p1ai")]
    [InlineData(null, null)]
    public void Normalize_should_return_canonical_hostname(string? value, string? expected) {
        _service.Normalize(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com:443")]
    [InlineData("203.0.113.10")]
    public void Normalize_should_reject_non_domain_values(string value) {
        var action = () => _service.Normalize(value);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Check_should_report_missing_domain() {
        var result = await _service.CheckAsync(null, null, null);

        result.State.Should().Be(DomainValidationState.NoDomain);
        result.ResolvedAddresses.Should().BeEmpty();
    }
}