using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;

public sealed class HostAddressJsonConverter : JsonConverter<HostAddress> {
    public override HostAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        try {
            return HostAddress.Parse(reader.GetString() ?? throw new JsonException("Host address cannot be null"));
        }
        catch (ArgumentException exception) {
            throw new JsonException("Host address must be a host with an optional valid port", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, HostAddress value, JsonSerializerOptions options) {
        writer.WriteStringValue(value.ToString());
    }
}