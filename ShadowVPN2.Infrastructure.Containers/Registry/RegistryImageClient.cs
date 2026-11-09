using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Infrastructure.Containers.Registry.Models;

namespace ShadowVPN2.Infrastructure.Containers.Registry;

public sealed class RegistryImageClient(IHttpClientFactory httpClientFactory) {
    public async Task<ImageDigestResolution> GetDigestAsync(ImageReference reference,
        ContainerImageInfo image, CancellationToken cancellationToken) {
        using var client = httpClientFactory.CreateClient(nameof(RegistryImageClient));
        var session = new RegistrySession(client, reference);
        var manifest = await session.ReadAsync(RegistryResource.Manifest, reference.Tag, null,
            RegistryJsonContext.Default.RegistryManifest, cancellationToken);
        return new ImageDigestResolution(manifest.Digest, GetPlatformDigest(manifest.Value, image));
    }

    public async Task<DateTimeOffset?> GetCreatedAsync(ImageReference reference, string digest,
        ContainerImageInfo image, CancellationToken cancellationToken) {
        if (!ImageDigest.IsValid(digest))
            throw new ArgumentException("A canonical sha256 image digest is required", nameof(digest));
        using var client = httpClientFactory.CreateClient(nameof(RegistryImageClient));
        var session = new RegistrySession(client, reference);
        var manifest = await session.ReadAsync(RegistryResource.Manifest, digest, digest,
            RegistryJsonContext.Default.RegistryManifest, cancellationToken);
        var platformDigest = GetPlatformDigest(manifest.Value, image);
        if (platformDigest is not null) {
            manifest = await session.ReadAsync(RegistryResource.Manifest, platformDigest, platformDigest,
                RegistryJsonContext.Default.RegistryManifest, cancellationToken);
        }

        var configDigest = manifest.Value.Config?.Digest;
        if (!ImageDigest.IsValid(configDigest))
            throw new InvalidOperationException("The image registry returned an invalid config digest");
        var config = await session.ReadAsync(RegistryResource.Blob, configDigest!, configDigest,
            RegistryJsonContext.Default.RegistryImageConfiguration, cancellationToken);
        return config.Value.CreatedAt;
    }

    private static string? GetPlatformDigest(RegistryManifest manifest, ContainerImageInfo image) {
        if (manifest.Manifests is not { } manifests) return null;
        foreach (var entry in manifests) {
            var platform = entry.Platform;
            if (platform is null || platform.Os != image.Os || platform.Architecture != image.Architecture)
                continue;
            if (!string.IsNullOrEmpty(image.Variant) && platform.Variant != image.Variant) continue;
            if (!ImageDigest.IsValid(entry.Digest))
                throw new InvalidOperationException("The image registry returned an invalid platform digest");
            return entry.Digest;
        }

        throw new InvalidOperationException("This image tag does not support the running container platform");
    }
}