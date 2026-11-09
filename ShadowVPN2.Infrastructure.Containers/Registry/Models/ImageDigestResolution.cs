namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

public readonly record struct ImageDigestResolution(string Digest, string? PlatformDigest);