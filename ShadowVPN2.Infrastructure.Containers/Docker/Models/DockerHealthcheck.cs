namespace ShadowVPN2.Infrastructure.Containers.Docker.Models;

public sealed class DockerHealthcheck {
    public string[]? Test { get; init; }
    public long Interval { get; init; }
    public long Timeout { get; init; }
    public long StartPeriod { get; init; }
    public int Retries { get; init; }
}