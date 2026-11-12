namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

public sealed class DockerContainerConfig {
    public required string Image { get; init; }
    public string[]? Env { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
    public DockerHealthcheck? Healthcheck { get; init; }
}