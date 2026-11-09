using System.Text.Json.Serialization;

namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

internal sealed class RegistryImageLabels {
    [JsonPropertyName("org.opencontainers.image.created")]
    [JsonConverter(typeof(RegistryTimestampConverter))]
    public DateTimeOffset? Created { get; init; }
}