using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;

public sealed class PortBindingJsonConverter : JsonConverter<PortBinding> {
    public override PortBinding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        try {
            if (reader.TokenType == JsonTokenType.Number) {
                if (!reader.TryGetInt32(out var port))
                    throw new JsonException("Port must be an integer from 1 to 65535");
                return new PortBinding(port);
            }

            var value = JsonSerializer.Deserialize<BindingObject>(ref reader, options);
            return new PortBinding(value.ListenPort, value.PortRangeEnd);
        }
        catch (ArgumentOutOfRangeException exception) {
            throw new JsonException("Port binding must contain ports from 1 to 65535 in ascending order", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, PortBinding value, JsonSerializerOptions options) {
        JsonSerializer.Serialize(writer, new BindingObject(value.ListenPort, value.PortRangeEnd), options);
    }

    private readonly record struct BindingObject(int ListenPort, int? PortRangeEnd);
}