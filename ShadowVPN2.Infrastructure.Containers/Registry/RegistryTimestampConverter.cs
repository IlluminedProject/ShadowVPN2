using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Infrastructure.Containers.Registry;

internal sealed class RegistryTimestampConverter : JsonConverter<DateTimeOffset?> {
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.String) {
            reader.Skip();
            return null;
        }

        if (reader.TryGetDateTimeOffset(out var created)) return created;
        return DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out created)
            ? created
            : null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options) {
        if (value is { } created) writer.WriteStringValue(created);
        else writer.WriteNullValue();
    }
}