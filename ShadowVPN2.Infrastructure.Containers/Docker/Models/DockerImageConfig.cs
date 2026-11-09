namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

public sealed class DockerImageConfig {
    public Dictionary<string, string>? Labels { get; init; }
}