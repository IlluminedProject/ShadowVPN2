using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data;
using ShadowVPN2.Entities;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class EntityPortSerializationTests {
    [Theory]
    [InlineData(typeof(Hysteria2GlobalSettings), 4443)]
    [InlineData(typeof(AwgGlobalSettings), 51821)]
    [InlineData(typeof(NaiveProxyGlobalSettings), 8443)]
    [InlineData(typeof(FreeTurnTransportSettings), 56000)]
    public void Legacy_numeric_listen_port_loads_and_serializes_as_binding(Type entityType, int port) {
        var settings = CreateSerializerSettings();
        var entity = JsonConvert.DeserializeObject($"{{\"ListenPort\":{port}}}", entityType, settings);

        Assert.NotNull(entity);
        Assert.Equal(new PortBinding(port), GetBinding(entity));
        Assert.Null(GetBinding(entity).PortRangeEnd);

        var json = JObject.Parse(JsonConvert.SerializeObject(entity, settings));
        Assert.Null(json.Property("PortBinding"));
        Assert.Null(json.Property("PortRangeEnd"));
        var bindingJson = Assert.IsType<JObject>(json["ListenPort"]);
        Assert.Equal(port, bindingJson.Value<int>("ListenPort"));
        Assert.Equal(JTokenType.Null, bindingJson["PortRangeEnd"]!.Type);

        var restored = JsonConvert.DeserializeObject(json.ToString(), entityType, settings);
        Assert.NotNull(restored);
        Assert.Equal(GetBinding(entity), GetBinding(restored));
    }

    [Theory]
    [InlineData("{\"ListenPort\":{\"ListenPort\":4443}}", null)]
    [InlineData("{\"ListenPort\":{\"ListenPort\":4443,\"PortRangeEnd\":null}}", null)]
    [InlineData("{\"ListenPort\":{\"ListenPort\":4443,\"PortRangeEnd\":4450}}", 4450)]
    public void Hysteria_binding_preserves_optional_range_end(string json, int? rangeEnd) {
        var settings = CreateSerializerSettings();
        var entity = JsonConvert.DeserializeObject<Hysteria2GlobalSettings>(json, settings);

        Assert.NotNull(entity);
        Assert.Equal(new PortBinding(4443, rangeEnd), entity.ListenPort);

        var serialized = JObject.Parse(JsonConvert.SerializeObject(entity, settings));
        Assert.Null(serialized.Property("PortBinding"));
        Assert.Null(serialized.Property("PortRangeEnd"));
        var bindingJson = Assert.IsType<JObject>(serialized["ListenPort"]);
        Assert.Equal(4443, bindingJson.Value<int>("ListenPort"));
        Assert.Equal(rangeEnd, bindingJson.Value<int?>("PortRangeEnd"));

        var restored = JsonConvert.DeserializeObject<Hysteria2GlobalSettings>(serialized.ToString(), settings);
        Assert.NotNull(restored);
        Assert.Equal(entity.ListenPort, restored.ListenPort);
    }

    private static JsonSerializerSettings CreateSerializerSettings() {
        return new JsonSerializerSettings {
            Converters = { new PortBindingRavenJsonConverter() }
        };
    }

    private static PortBinding GetBinding(object entity) {
        return entity switch {
            ProtocolGlobalSettings protocol => protocol.ListenPort,
            TransportSettings transport => transport.ListenPort,
            _ => throw new ArgumentException("Expected protocol or transport settings", nameof(entity))
        };
    }
}