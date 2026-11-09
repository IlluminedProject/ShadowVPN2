using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class PortBindingRavenSerializationTests {
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