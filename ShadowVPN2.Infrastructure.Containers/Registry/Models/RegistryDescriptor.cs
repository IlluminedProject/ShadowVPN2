using System.Text.Json.Serialization;

namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

internal sealed class RegistryDescriptor {
    [JsonPropertyName("digest")] public string? Digest { get; init; }

    [JsonPropertyName("platform")] public RegistryPlatform? Platform { get; init; }
}