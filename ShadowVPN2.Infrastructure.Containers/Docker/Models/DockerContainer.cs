namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

public sealed class DockerContainer {
    public required string Id { get; init; }
    public required string Image { get; init; }
    public required DockerContainerState State { get; init; }
    public required DockerContainerConfig Config { get; init; }
    public required DockerHostConfig HostConfig { get; init; }
}