using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

public sealed class DockerImage {
    public string Id { get; init; } = string.Empty;
    public required string[] RepoDigests { get; init; }
    public string? Os { get; init; }
    public string? Architecture { get; init; }
    public string? Variant { get; init; }
    public JsonElement Created { get; init; }
    public DockerImageConfig? Config { get; init; }

    [JsonIgnore]
    public DateTimeOffset? CreatedAt {
        get {
            if (Created.ValueKind == JsonValueKind.String &&
                (Created.TryGetDateTimeOffset(out var created) || TryParseCreated(Created.GetString(), out created)))
                return created;
            return Config?.Labels is { } labels &&
                   labels.TryGetValue("org.opencontainers.image.created", out var label) &&
                   TryParseCreated(label, out created)
                ? created
                : null;
        }
    }

    private static bool TryParseCreated(string? value, out DateTimeOffset created) {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out created);
    }
}