namespace ShadowVPN2.Data.Cluster;

internal sealed record UpdaterResponse(Guid UpdateId, string ImageDigest, string Status, string? Error);