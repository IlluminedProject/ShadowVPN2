namespace ShadowVPN2.Updater.Docker;

public sealed class DockerContainer {
    public required string Id { get; init; }
    public required string Image { get; init; }
    public required DockerContainerState State { get; init; }
    public required DockerContainerConfig Config { get; init; }
    public required DockerHostConfig HostConfig { get; init; }
}

public sealed class DockerContainerState {
    public required bool Running { get; init; }
    public DockerHealthState? Health { get; init; }
}

public sealed class DockerHealthState {
    public required string Status { get; init; }
}

public sealed class DockerContainerConfig {
    public required string Image { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
}

public sealed class DockerHostConfig {
    public required DockerRestartPolicy RestartPolicy { get; init; }
}

public sealed class DockerRestartPolicy {
    public required string Name { get; init; }
    public int MaximumRetryCount { get; init; }
}

public sealed class DockerImage {
    public required string[] RepoDigests { get; init; }
    public string? Os { get; init; }
    public string? Architecture { get; init; }
    public string? Variant { get; init; }
}

public sealed class DockerCreateResponse {
    public required string Id { get; init; }
}
