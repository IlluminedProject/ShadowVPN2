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
    public void Invalid_header_is_rejected(string? header) {
        var result = new AwgSettingsValidator().Validate(new AwgProtocolSettingsDto { H1 = header });
        result.Errors.Should().Contain(error => error.PropertyName == "H1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Absent_or_blank_headers_are_valid(string? header) {
        var result = new AwgSettingsValidator().Validate(new AwgProtocolSettingsDto {
            H1 = header, H2 = header, H3 = header, H4 = header
        });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(AwgProtocolSettingsDto))]
    [InlineData(typeof(AwgClusterSettings))]
    [InlineData(typeof(AwgGlobalSettings))]
    public void All_awg_parameters_are_absent_by_default_and_valid(Type settingsType) {
        var settings = (IAwgSettings)Activator.CreateInstance(settingsType)!;

        settings.Jc.Should().BeNull();
        settings.Jmin.Should().BeNull();
        settings.Jmax.Should().BeNull();
        settings.S1.Should().BeNull();
        settings.S2.Should().BeNull();
        settings.S3.Should().BeNull();
        settings.S4.Should().BeNull();
        settings.H1.Should().BeNull();
        settings.H2.Should().BeNull();
        settings.H3.Should().BeNull();
        settings.H4.Should().BeNull();
        settings.I1.Should().BeNull();
        settings.I2.Should().BeNull();
        settings.I3.Should().BeNull();
        settings.I4.Should().BeNull();
        settings.I5.Should().BeNull();
        new AwgSettingsValidator().Validate(settings).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("0", null, null, null)]
    [InlineData(null, "0", null, null)]
    [InlineData(null, null, "0", null)]
    [InlineData(null, null, null, "0")]
    [InlineData("0-10", null, "11-20", null)]
    public void Partial_headers_are_valid(string? h1, string? h2, string? h3, string? h4) {
        var result = new AwgSettingsValidator().Validate(new AwgProtocolSettingsDto {
            H1 = h1, H2 = h2, H3 = h3, H4 = h4
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Header_ranges_must_not_share_an_endpoint() {
        var result = new AwgSettingsValidator().Validate(new AwgProtocolSettingsDto { H1 = "10-20", H2 = "20-30" });
        result.Errors.Select(error => error.PropertyName).Should().BeEquivalentTo("H1", "H2");
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("10-30", "15-20")]
    [InlineData("15", "10-20")]
    public void Partial_overlapping_headers_are_rejected(string h1, string h4) {
        var result = new AwgSettingsValidator().Validate(new AwgProtocolSettingsDto { H1 = h1, H4 = h4 });

        result.Errors.Select(error => error.PropertyName).Should().BeEquivalentTo("H1", "H4");
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
        const string json = "{\"PortBinding\":51821,\"Jc\":4294967295,\"Jmin\":2147483648,\"Jmax\":4294967295}";
        var protocol = JsonSerializer.Deserialize<AwgProtocolSettingsDto>(json)!;
        var roundTrip = JsonSerializer.Deserialize<AwgProtocolSettingsDto>(
            JsonSerializer.Serialize(protocol))!;
        roundTrip.Jc.Should().Be(uint.MaxValue);
        roundTrip.Jmin.Should().Be(2147483648u);
        roundTrip.Jmax.Should().Be(uint.MaxValue);
    }

    [Theory]
    [InlineData(typeof(AwgProtocolSettingsDto))]
    [InlineData(typeof(AwgClusterSettings))]
    [InlineData(typeof(AwgGlobalSettings))]
    public void Json_preserves_absent_null_and_zero_awg_parameters(Type settingsType) {
        var absent = (IAwgSettings)JsonSerializer.Deserialize("{\"PortBinding\":51821}", settingsType)!;
        var explicitNull = (IAwgSettings)JsonSerializer.Deserialize(
            "{\"PortBinding\":51821,\"Jc\":null,\"Jmin\":null,\"Jmax\":null,\"S1\":null,\"S2\":null,\"S3\":null,\"S4\":null,\"H1\":null,\"H2\":null,\"H3\":null,\"H4\":null}",
            settingsType)!;
        var explicitZero = (IAwgSettings)JsonSerializer.Deserialize(
            "{\"PortBinding\":51821,\"Jc\":0,\"Jmin\":0,\"Jmax\":0,\"S1\":0,\"S2\":0,\"S3\":0,\"S4\":0,\"H1\":\"0\",\"H2\":\"0\",\"H3\":\"0\",\"H4\":\"0\"}",
            settingsType)!;

        using var absentJson = JsonDocument.Parse(JsonSerializer.Serialize(absent, settingsType));
        using var nullJson = JsonDocument.Parse(JsonSerializer.Serialize(explicitNull, settingsType));
        using var zeroJson = JsonDocument.Parse(JsonSerializer.Serialize(explicitZero, settingsType));
        foreach (var field in new[] { "Jc", "Jmin", "Jmax", "S1", "S2", "S3", "S4", "H1", "H2", "H3", "H4" }) {
            absentJson.RootElement.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null);
            nullJson.RootElement.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null);
            if (field.StartsWith('H'))
                zeroJson.RootElement.GetProperty(field).GetString().Should().Be("0");
            else
                zeroJson.RootElement.GetProperty(field).GetUInt32().Should().Be(0);
        }

        var restored = (IAwgSettings)JsonSerializer.Deserialize(zeroJson.RootElement.GetRawText(), settingsType)!;
        restored.Jc.Should().Be(0);
        restored.Jmin.Should().Be(0);
        restored.Jmax.Should().Be(0);
        restored.S1.Should().Be(0);
        restored.S2.Should().Be(0);
        restored.S3.Should().Be(0);
        restored.S4.Should().Be(0);
        restored.H1.Should().Be("0");
        restored.H2.Should().Be("0");
        restored.H3.Should().Be("0");
        restored.H4.Should().Be("0");
    }

    [Fact]
    public void Absent_awg_parameters_are_plain_wireguard() {
        new AwgProtocolSettingsDto().IsAmneziaWg.Should().BeFalse();
        new AwgGlobalSettings().IsAmneziaWg.Should().BeFalse();
    }

    [Theory]
    [InlineData("Jc", "0")]
    [InlineData("Jmin", "0")]
    [InlineData("Jmax", "0")]
    [InlineData("S1", "0")]
    [InlineData("S2", "0")]
    [InlineData("S3", "0")]
    [InlineData("S4", "0")]
    [InlineData("H1", "\"0\"")]
    [InlineData("H1", "\"1\"")]
    [InlineData("H2", "\"2\"")]
    [InlineData("H3", "\"3\"")]
    [InlineData("H4", "\"4\"")]
    [InlineData("I1", "\"<b 0x01>\"")]
    [InlineData("I2", "\"<b 0x01>\"")]
    [InlineData("I3", "\"<b 0x01>\"")]
    [InlineData("I4", "\"<b 0x01>\"")]
    [InlineData("I5", "\"<b 0x01>\"")]
    public void Any_present_awg_parameter_enables_amnezia(string field, string value) {
        var json = $"{{\"{field}\":{value}}}";

        JsonSerializer.Deserialize<AwgProtocolSettingsDto>(json)!.IsAmneziaWg.Should().BeTrue();
        JsonSerializer.Deserialize<AwgGlobalSettings>(json)!.IsAmneziaWg.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Blank_optional_strings_do_not_enable_amnezia(string? value) {
        var protocol = new AwgProtocolSettingsDto {
            H1 = value, H2 = value, H3 = value, H4 = value,
            I1 = value, I2 = value, I3 = value, I4 = value, I5 = value
        };
        var entity = new AwgGlobalSettings {
            H1 = value, H2 = value, H3 = value, H4 = value,
            I1 = value, I2 = value, I3 = value, I4 = value, I5 = value
        };

        protocol.IsAmneziaWg.Should().BeFalse();
        entity.IsAmneziaWg.Should().BeFalse();
    }
}