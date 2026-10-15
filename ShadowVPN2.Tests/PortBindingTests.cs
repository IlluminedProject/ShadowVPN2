using System.Globalization;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Protocols;
using ShadowVPN2.Data;
using Xunit;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace ShadowVPN2.Tests;

public sealed class PortBindingTests {
    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(65536, null)]
    [InlineData(443, 0)]
    [InlineData(443, -1)]
    [InlineData(443, 65536)]
    [InlineData(4450, 4443)]
    public void Rejects_invalid_listener_or_range_end(int listenPort, int? portRangeEnd) {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortBinding(listenPort, portRangeEnd));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(443)]
    [InlineData(65535)]
    public void Equal_range_end_normalizes_to_single_listener(int port) {
        var binding = new PortBinding(port, port);

        Assert.Equal(new PortBinding(port), binding);
        Assert.Null(binding.PortRangeEnd);
        Assert.Equal(port.ToString(CultureInfo.InvariantCulture), binding.ToString());
    }

    [Theory]
    [InlineData("1", 1, null, "1")]
    [InlineData("65535", 65535, null, "65535")]
    [InlineData("443", 443, null, "443")]
    [InlineData("443-443", 443, null, "443")]
    [InlineData("1-65535", 1, 65535, "1-65535")]
    [InlineData(" 4443 - 4450 ", 4443, 4450, "4443-4450")]
    public void Parses_and_round_trips_single_ports_and_ranges(string text, int listenPort, int? portRangeEnd,
        string canonical) {
        Assert.True(PortBinding.TryParse(text, out var binding));
        Assert.Equal(new PortBinding(listenPort, portRangeEnd), binding);
        Assert.Equal(canonical, binding.ToString());
        Assert.True(PortBinding.TryParse(binding.ToString(), out var restored));
        Assert.Equal(binding, restored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("0-443")]
    [InlineData("443-65536")]
    [InlineData("4450-4443")]
    [InlineData("1-2-3")]
    [InlineData("4443:5000-5010")]
    [InlineData("4443:4443")]
    public void Rejects_invalid_ports_ranges_and_backend_range_syntax(string? text) {
        Assert.False(PortBinding.TryParse(text, out _));
    }

    [Fact]
    public void Range_starts_at_listener_and_includes_both_boundaries() {
        var binding = new PortBinding(4443, 4450);

        Assert.Equal(4443, binding.ListenPort);
        Assert.Equal(4450, binding.PortRangeEnd);
        Assert.True(binding.Contains(4443));
        Assert.True(binding.Contains(4450));
        Assert.False(binding.Contains(4442));
        Assert.False(binding.Contains(4451));
    }

    [Theory]
    [InlineData(4443, null, true)]
    [InlineData(4450, null, true)]
    [InlineData(4442, null, false)]
    [InlineData(4451, null, false)]
    [InlineData(4430, 4443, true)]
    [InlineData(4450, 4460, true)]
    [InlineData(4430, 4442, false)]
    [InlineData(4451, 4460, false)]
    [InlineData(4444, 4449, true)]
    [InlineData(4430, 4460, true)]
    public void Overlap_is_symmetric_and_includes_shared_boundaries(int listenPort, int? portRangeEnd,
        bool expected) {
        var binding = new PortBinding(4443, 4450);
        var other = new PortBinding(listenPort, portRangeEnd);

        Assert.Equal(expected, binding.Overlaps(other));
        Assert.Equal(expected, other.Overlaps(binding));
    }

    [Fact]
    public void Single_listener_contains_and_overlaps_only_its_port() {
        var binding = new PortBinding(443);

        Assert.True(binding.Contains(443));
        Assert.False(binding.Contains(442));
        Assert.False(binding.Contains(444));
        Assert.True(binding.Overlaps(new PortBinding(443)));
        Assert.False(binding.Overlaps(new PortBinding(444)));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("443", 443)]
    [InlineData("65535", 65535)]
    public void System_text_json_reads_numeric_listener(string json, int port) {
        var binding = JsonSerializer.Deserialize<PortBinding>(json);

        Assert.Equal(new PortBinding(port), binding);
        Assert.Null(binding.PortRangeEnd);
    }

    [Fact]
    public void System_text_json_reads_numeric_binding_in_Hysteria_settings_with_web_options() {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var settings = JsonSerializer.Deserialize<Hysteria2ProtocolSettingsDto>("{\"portBinding\":443}", options);

        Assert.NotNull(settings);
        Assert.Equal(new PortBinding(443), settings.PortBinding);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("443.5")]
    [InlineData("\"443\"")]
    [InlineData("null")]
    public void System_text_json_rejects_invalid_numeric_or_non_binding_values(string json) {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PortBinding>(json));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Hysteria2ProtocolSettingsDto>("{\"portBinding\":" + json + "}", options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4450)]
    public void System_text_json_round_trips_Hysteria_binding_object_with_web_options(int? portRangeEnd) {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var settings = new Hysteria2ProtocolSettingsDto { PortBinding = new PortBinding(4443, portRangeEnd) };
        var json = JsonSerializer.Serialize(settings, options);
        using var document = JsonDocument.Parse(json);
        var binding = document.RootElement.GetProperty("portBinding");

        Assert.Equal(4443, binding.GetProperty("listenPort").GetInt32());
        if (portRangeEnd is { } end)
            Assert.Equal(end, binding.GetProperty("portRangeEnd").GetInt32());
        else
            Assert.Equal(JsonValueKind.Null, binding.GetProperty("portRangeEnd").ValueKind);
        var restored = JsonSerializer.Deserialize<Hysteria2ProtocolSettingsDto>(json, options);
        Assert.NotNull(restored);
        Assert.Equal(settings.PortBinding, restored.PortBinding);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4450)]
    public void System_text_json_round_trip_serializes_only_listener_and_range_end(int? portRangeEnd) {
        var binding = new PortBinding(4443, portRangeEnd);
        var json = JsonSerializer.Serialize(binding);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(4443, root.GetProperty("ListenPort").GetInt32());
        if (portRangeEnd is { } end)
            Assert.Equal(end, root.GetProperty("PortRangeEnd").GetInt32());
        else
            Assert.Equal(JsonValueKind.Null, root.GetProperty("PortRangeEnd").ValueKind);
        Assert.False(root.TryGetProperty("PortRange", out _));
        Assert.Equal(2, root.EnumerateObject().Count());
        Assert.Equal(binding, JsonSerializer.Deserialize<PortBinding>(json));
    }

    [Fact]
    public void Newtonsoft_reads_numeric_listener_with_host_binding_converter() {
        var settings = new JsonSerializerSettings {
            Converters = { new PortBindingRavenJsonConverter() }
        };

        var binding = JsonConvert.DeserializeObject<PortBinding>("443", settings);

        Assert.Equal(new PortBinding(443), binding);
        Assert.Null(binding.PortRangeEnd);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4450)]
    public void Newtonsoft_round_trip_uses_host_binding_converter(int? portRangeEnd) {
        var settings = new JsonSerializerSettings {
            Converters = { new PortBindingRavenJsonConverter() }
        };
        var binding = new PortBinding(4443, portRangeEnd);
        var json = JsonConvert.SerializeObject(binding, settings);
        var root = JObject.Parse(json);

        Assert.Equal(4443, root.Value<int>("ListenPort"));
        Assert.True(root.ContainsKey("PortRangeEnd"));
        Assert.Equal(portRangeEnd, root.Value<int?>("PortRangeEnd"));
        Assert.False(root.ContainsKey("PortRange"));
        Assert.Equal(2, root.Count);
        Assert.Equal(binding, JsonConvert.DeserializeObject<PortBinding>(json, settings));
    }
}