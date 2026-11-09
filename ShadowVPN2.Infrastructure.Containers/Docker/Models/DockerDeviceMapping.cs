namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

public sealed class DockerDeviceMapping {
    public required string PathOnHost { get; init; }
    public required string PathInContainer { get; init; }
    public required string CgroupPermissions { get; init; }
}