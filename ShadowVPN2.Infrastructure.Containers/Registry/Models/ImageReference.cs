namespace ShadowVPN2.Infrastructure.Containers.Registry.Models;

public readonly record struct ImageReference(string Registry, string Repository, string Tag) {
    public static ImageReference Parse(string reference) {
        if (reference.Contains('@'))
            throw new InvalidOperationException("Digest-pinned images do not support update checks");
        var slash = reference.IndexOf('/');
        var registry = slash > 0 && (reference[..slash].Contains('.') || reference[..slash].Contains(':') ||
                                     reference[..slash] == "localhost")
            ? reference[..slash]
            : "registry-1.docker.io";
        var repository = registry == "registry-1.docker.io" && !(slash > 0 && reference[..slash] == registry)
            ? reference
            : reference[(slash + 1)..];
        if (registry == "docker.io") registry = "registry-1.docker.io";
        var colon = repository.LastIndexOf(':');
        var tag = colon > repository.LastIndexOf('/') ? repository[(colon + 1)..] : "latest";
        if (colon > repository.LastIndexOf('/')) repository = repository[..colon];
        if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(tag) || reference.Contains("://"))
            throw new InvalidOperationException("The configured application image reference is invalid");
        if (!repository.Contains('/') && registry == "registry-1.docker.io") repository = "library/" + repository;
        return new ImageReference(registry, repository, tag);
    }
}