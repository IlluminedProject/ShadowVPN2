using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShadowVPN2.Contracts.Nodes;
using ShadowVPN2.Data;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class DomainValidationServiceTests {
    private readonly DomainValidationService _service = new(NullLogger<DomainValidationService>.Instance);

    [Fact]
    public async Task Check_should_report_missing_domain() {
        var result = await _service.CheckAsync(null, null, null);

        result.State.Should().Be(DomainValidationState.NoDomain);
        result.ResolvedAddresses.Should().BeEmpty();
    }
}