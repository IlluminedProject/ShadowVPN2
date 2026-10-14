using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Nodes;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class UpdateNodeRequestTests {
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_node_name_should_be_rejected(string? name) {
        var request = new UpdateNodeRequest { Name = name! };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(request, new ValidationContext(request), results, true).Should().BeFalse();
        results.Should().Contain(result => result.MemberNames.Contains(nameof(request.Name)));
    }

    [Fact]
    public void Node_name_should_allow_spaces_and_unicode() {
        var request = new UpdateNodeRequest { Name = "City 1" };

        Validator.TryValidateObject(request, new ValidationContext(request), [], true).Should().BeTrue();
    }
}