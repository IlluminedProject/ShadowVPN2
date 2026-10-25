using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Cluster;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Protocols;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using Xunit;
using UpdateProtocolsSettingsRequest = ShadowVPN2.Contracts.Protocols.UpdateProtocolsSettingsRequest;

namespace ShadowVPN2.Tests;

public sealed class AwgSettingsValidationTests {
    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void Invalid_padding_is_rejected_for_mesh_and_protocol(int padding) {
        var mesh = new EntityGlobalConfiguration();
        mesh.AwgSettings.S1 = padding;
        var validateMesh = () => GlobalConfigurationValidator.ValidateMeshPort(mesh);
        validateMesh.Should().Throw<ArgumentException>();

        var configuration = new EntityGlobalConfiguration {
            Protocols = [
                new AwgGlobalSettings {
                    Id = Guid.NewGuid(), ListenPort = new PortBinding(51821), S4 = padding
                }
            ]
        };
        var validateProtocol = () => GlobalConfigurationValidator.Validate(configuration);
        validateProtocol.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("4294967296")]
    [InlineData("-1")]
    [InlineData("100-99")]
    [InlineData("1-2-3")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_header_is_rejected(string? header) {
        var result = new AwgSettingsValidator().Validate(new AwgProtocolSettingsDto { H1 = header! });
        result.Errors.Should().Contain(error => error.PropertyName == "H1");
    }

    [Fact]
    public void Header_ranges_must_not_share_an_endpoint() {
        var result = new AwgSettingsValidator().Validate(new AwgProtocolSettingsDto { H1 = "10-20", H2 = "20-30" });
        result.Errors.Select(error => error.PropertyName).Should().BeEquivalentTo("H1", "H2");
    }

    [Theory]
    [InlineData("Jc", "-1")]
    [InlineData("Jmin", "-1")]
    [InlineData("Jmax", "-1")]
    [InlineData("Jc", "4294967296")]
    [InlineData("Jmin", "4294967296")]
    [InlineData("Jmax", "4294967296")]
    public void Junk_parameters_outside_uint_range_are_rejected(string field, string value) {
        var json = $"{{\"{field}\":{value}}}";
        var parseProtocol = () => JsonSerializer.Deserialize<AwgProtocolSettingsDto>(json);
        var parseMesh = () => JsonSerializer.Deserialize<AwgClusterSettings>(json);
        parseProtocol.Should().Throw<JsonException>();
        parseMesh.Should().Throw<JsonException>();
    }

    [Fact]
    public void Numeric_boundaries_and_disjoint_header_ranges_are_accepted() {
        var result = new AwgSettingsValidator().Validate(new AwgProtocolSettingsDto {
            Jc = uint.MaxValue, Jmin = uint.MaxValue, Jmax = uint.MaxValue, S2 = 65535, S3 = 65535,
            H1 = "0", H2 = "1-2", H3 = "3", H4 = "4294967295"
        });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Protocol_collection_reports_the_invalid_item_and_field() {
        var result = new UpdateProtocolsSettingsValidator().Validate(new UpdateProtocolsSettingsRequest {
            Protocols = [new AwgProtocolSettingsDto(), new AwgProtocolSettingsDto { S4 = 65536 }]
        });
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Protocols[1].S4");
    }

    [Fact]
    public void Junk_parameters_preserve_full_uint_range_in_json() {
        const string json = "{\"Jc\":4294967295,\"Jmin\":2147483648,\"Jmax\":4294967295}";
        var protocol = JsonSerializer.Deserialize<AwgProtocolSettingsDto>(json)!;
        var roundTrip = JsonSerializer.Deserialize<AwgProtocolSettingsDto>(
            JsonSerializer.Serialize(protocol))!;
        roundTrip.Jc.Should().Be(uint.MaxValue);
        roundTrip.Jmin.Should().Be(2147483648u);
        roundTrip.Jmax.Should().Be(uint.MaxValue);
    }
}