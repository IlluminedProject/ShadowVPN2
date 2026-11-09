using System.Text.Json;
using System.Text.Json.Serialization;

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
    public string[]? Env { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
}

public sealed class DockerHostConfig {
    public required DockerRestartPolicy RestartPolicy { get; init; }
    public string? NetworkMode { get; init; }
    public bool Privileged { get; init; }
    public string[]? CapAdd { get; init; }
    public string[]? CapDrop { get; init; }
    public DockerDeviceMapping[]? Devices { get; init; }
    public string[]? Binds { get; init; }
    public JsonElement[]? Mounts { get; init; }
    public Dictionary<string, string[]>? PortBindings { get; init; }
    public bool PublishAllPorts { get; init; }
    public string[]? Dns { get; init; }
    public string[]? DnsSearch { get; init; }
    public string[]? ExtraHosts { get; init; }
    public string? PidMode { get; init; }
    public string? IpcMode { get; init; }
    public string? UtsMode { get; init; }
    public bool Init { get; init; }
    public string[]? SecurityOpt { get; init; }
}

public sealed class DockerDeviceMapping {
    public required string PathOnHost { get; init; }
    public required string PathInContainer { get; init; }
    public required string CgroupPermissions { get; init; }
}

public sealed class DockerRestartPolicy {
    public required string Name { get; init; }
    public int MaximumRetryCount { get; init; }
}

public sealed class DockerImage {
    public string Id { get; init; } = string.Empty;
    public required string[] RepoDigests { get; init; }
    public string? Os { get; init; }
    public string? Architecture { get; init; }
    public string? Variant { get; init; }
}

public sealed class DockerCreateRequest {
    public required string Image { get; init; }
    public required DockerCreateHostConfig HostConfig { get; init; }
    public string[]? Env { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
}

public sealed class DockerCreateHostConfig {
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DockerRestartPolicy? RestartPolicy { get; init; }

    public string? NetworkMode { get; init; }
    public bool Privileged { get; init; }
    public string[]? CapAdd { get; init; }
    public string[]? CapDrop { get; init; }
    public DockerDeviceMapping[]? Devices { get; init; }
    public string[]? Binds { get; init; }
    public JsonElement[]? Mounts { get; init; }
    public Dictionary<string, string[]>? PortBindings { get; init; }
    public bool PublishAllPorts { get; init; }
    public string[]? Dns { get; init; }
    public string[]? DnsSearch { get; init; }
    public string[]? ExtraHosts { get; init; }
    public string? PidMode { get; init; }
    public string? IpcMode { get; init; }
    public string? UtsMode { get; init; }
    public bool Init { get; init; }
    public string[]? SecurityOpt { get; init; }
    public int MaximumRetryCount { get; init; }
}

public sealed class DockerCreateResponse {
    public required string Id { get; init; }
}