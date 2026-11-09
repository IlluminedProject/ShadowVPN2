namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

public sealed class DockerCreateRequest {
    public required string Image { get; init; }
    public required DockerCreateHostConfig HostConfig { get; init; }
    public string[]? Env { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
}