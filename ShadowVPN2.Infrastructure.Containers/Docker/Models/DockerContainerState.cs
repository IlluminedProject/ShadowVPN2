namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

public sealed class DockerContainerState {
    public required bool Running { get; init; }
    public DockerHealthState? Health { get; init; }
}