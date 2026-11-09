namespace ShadowVPN2.Contracts.Nodes;

public sealed record NodeRuntimeMetadata {
    public string? ContainerImageDigest { get; init; }
}