using System.Text.Json.Serialization;

namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

internal sealed class RegistryManifest {
    [JsonPropertyName("manifests")] public RegistryDescriptor[]? Manifests { get; init; }

    [JsonPropertyName("config")] public RegistryDescriptor? Config { get; init; }
}