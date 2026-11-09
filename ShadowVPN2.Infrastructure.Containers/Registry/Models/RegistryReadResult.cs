namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

internal readonly record struct RegistryReadResult<T>(T Value, string Digest);