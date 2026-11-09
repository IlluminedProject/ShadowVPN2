using System.Text.Json.Serialization;

namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

internal sealed class RegistryPlatform {
    [JsonPropertyName("os")] public string? Os { get; init; }

    [JsonPropertyName("architecture")] public string? Architecture { get; init; }

    [JsonPropertyName("variant")] public string? Variant { get; init; }
}