using System.Text.Json.Serialization;

namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

internal sealed class RegistryContainerConfiguration {
    [JsonPropertyName("Labels")] public RegistryImageLabels? Labels { get; init; }
}