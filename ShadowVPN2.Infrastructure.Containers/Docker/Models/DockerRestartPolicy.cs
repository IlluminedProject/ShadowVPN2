namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

public sealed class DockerRestartPolicy {
    public required string Name { get; init; }
    public int MaximumRetryCount { get; init; }
}