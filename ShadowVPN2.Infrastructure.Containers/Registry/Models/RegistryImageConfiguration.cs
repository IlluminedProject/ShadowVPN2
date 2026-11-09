using System.Text.Json.Serialization;

namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

internal sealed class RegistryImageConfiguration {
    [JsonPropertyName("created")]
    [JsonConverter(typeof(RegistryTimestampConverter))]
    public DateTimeOffset? Created { get; init; }

    [JsonPropertyName("config")] public RegistryContainerConfiguration? Config { get; init; }

    [JsonIgnore]
    public DateTimeOffset? CreatedAt {
        get => Created ?? Config?.Labels?.Created;
    }
}