using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;

public sealed class HostNameJsonConverter : JsonConverter<HostName> {
    public override HostName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        try {
            return HostName.Parse(reader.GetString() ?? throw new JsonException("Host name cannot be null"));
        }
        catch (ArgumentException exception) {
            throw new JsonException("Host name must be a DNS name or IP without a port", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, HostName value, JsonSerializerOptions options) {
        writer.WriteStringValue(value.Value);
    }
}