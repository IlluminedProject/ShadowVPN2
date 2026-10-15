using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data;

public sealed class PortBindingRavenJsonConverter : JsonConverter<PortBinding> {
    public override PortBinding ReadJson(JsonReader reader, Type objectType, PortBinding existingValue,
        bool hasExistingValue, JsonSerializer serializer) {
        if (reader.TokenType == JsonToken.Integer)
            return new PortBinding(Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture));
        var value = JObject.Load(reader);
        return new PortBinding(value.Value<int>(nameof(PortBinding.ListenPort)),
            value.Value<int?>(nameof(PortBinding.PortRangeEnd)));
    }

    public override void WriteJson(JsonWriter writer, PortBinding value, JsonSerializer serializer) {
        writer.WriteStartObject();
        writer.WritePropertyName(nameof(PortBinding.ListenPort));
        writer.WriteValue(value.ListenPort);
        writer.WritePropertyName(nameof(PortBinding.PortRangeEnd));
        writer.WriteValue(value.PortRangeEnd);
        writer.WriteEndObject();
    }
}