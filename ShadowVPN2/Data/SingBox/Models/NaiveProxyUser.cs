using System.Text.Json.Serialization;

namespace ShadowVPN2.Data.SingBox.Models;

public sealed class NaiveProxyUser {
    [JsonPropertyName("username")] public string Username { get; set; } = null!;

    [JsonPropertyName("password")] public string Password { get; set; } = null!;
}