using System.Text.Json;
using AwesomeAssertions;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Protocols;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class AwgGlobalSettingsTests {
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

        JsonSerializer.Deserialize<AwgGlobalSettings>(json)!.IsAmneziaWg.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Blank_optional_strings_do_not_enable_amnezia(string? value) {
        var entity = new AwgGlobalSettings {
            H1 = value, H2 = value, H3 = value, H4 = value,
            I1 = value, I2 = value, I3 = value, I4 = value, I5 = value
        };

        entity.IsAmneziaWg.Should().BeFalse();
    }
}