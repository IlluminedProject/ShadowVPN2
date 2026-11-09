using System.Text.Json.Serialization;

namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

internal sealed class RegistryTokenResponse {
    [JsonPropertyName("token")] public string? Token { get; init; }

    [JsonPropertyName("access_token")] public string? AccessToken { get; init; }
}