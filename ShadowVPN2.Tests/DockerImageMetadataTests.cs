using System.Text.Json;
using ShadowVPN2.Updater.Docker;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class DockerImageMetadataTests {
    [Theory]
    [InlineData(
        "{\"RepoDigests\":[],\"Created\":\"2026-10-15T12:30:00.123456700Z\",\"Config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2025-01-01T00:00:00Z\"}}}",
        "2026-10-15T12:30:00.1234567Z")]
    [InlineData("{\"RepoDigests\":[],\"Created\":\"2026-11-08T01:49:15.56850731Z\"}", "2026-11-08T01:49:15.5685073Z")]
    [InlineData(
        "{\"RepoDigests\":[],\"Config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2026-10-15T15:30:00+03:00\"}}}",
        "2026-10-15T12:30:00Z")]
    [InlineData(
        "{\"RepoDigests\":[],\"Created\":\"invalid\",\"Config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2026-10-15T12:30:00Z\"}}}",
        "2026-10-15T12:30:00Z")]
    [InlineData(
        "{\"RepoDigests\":[],\"Created\":123,\"Config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2026-10-15T12:30:00Z\"}}}",
        "2026-10-15T12:30:00Z")]
    [InlineData("{\"RepoDigests\":[]}", null)]
    [InlineData("{\"RepoDigests\":[],\"Created\":null}", null)]
    [InlineData("{\"RepoDigests\":[],\"Created\":{}}", null)]
    [InlineData("{\"RepoDigests\":[],\"Created\":\"invalid\"}", null)]
    [InlineData(
        "{\"RepoDigests\":[],\"Created\":\"invalid\",\"Config\":{\"Labels\":{\"org.opencontainers.image.created\":\"invalid\"}}}",
        null)]
    public void CreatedAt_prefers_image_created_and_falls_back_to_image_config_label(string json, string? expected) {
        var image = JsonSerializer.Deserialize(json, DockerJsonContext.Default.DockerImage);

        Assert.NotNull(image);
        Assert.Equal(expected is null ? null : DateTimeOffset.Parse(expected), image.CreatedAt);
    }
}