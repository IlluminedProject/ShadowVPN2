using System.Text.Json;

namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

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