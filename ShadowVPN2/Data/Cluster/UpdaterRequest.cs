namespace ShadowVPN2.Data.Cluster;

internal sealed record UpdaterRequest(string Operation, Guid UpdateId, string ImageDigest);