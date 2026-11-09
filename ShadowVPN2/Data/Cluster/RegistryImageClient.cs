using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Data.Cluster;

public sealed class RegistryImageClient(IHttpClientFactory httpClientFactory) {
    private const string Accept =
        "application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json";

    public static (string Registry, string Repository, string Tag) ParseReference(string reference) {
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
        if (!repository.Contains('/') && registry == "registry-1.docker.io") repository = "library/" + repository;
        if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(tag) || reference.Contains("://"))
            throw new InvalidOperationException("The configured application image reference is invalid");
        return (registry, repository, tag);
    }

    public async Task<(string Digest, string? PlatformDigest)> GetDigestAsync(string reference,
        ContainerImageInfo image,
        CancellationToken cancellationToken) {
        var (registry, repository, tag) = ParseReference(reference);
        using var client = httpClientFactory.CreateClient(nameof(RegistryImageClient));
        var session = new RegistrySession(client, registry, repository);
        var (bytes, digest) = await session.ReadAsync("manifests", tag, null, cancellationToken);
        using var manifest = JsonDocument.Parse(bytes);
        return (digest, GetPlatformDigest(manifest.RootElement, image));
    }

    public async Task<DateTimeOffset?> GetCreatedAsync(string repository, string digest,
        ContainerImageInfo image, CancellationToken cancellationToken) {
        if (!ImageDigest.IsValid(digest))
            throw new ArgumentException("A canonical sha256 image digest is required", nameof(digest));
        var (registry, imageRepository, _) = ParseReference(repository);
        using var client = httpClientFactory.CreateClient(nameof(RegistryImageClient));
        var session = new RegistrySession(client, registry, imageRepository);
        var (bytes, _) = await session.ReadAsync("manifests", digest, digest, cancellationToken);
        using var manifest = JsonDocument.Parse(bytes);
        var platformDigest = GetPlatformDigest(manifest.RootElement, image);
        JsonDocument? platformManifest = null;
        try {
            var root = manifest.RootElement;
            if (platformDigest is not null) {
                var (platformBytes, _) =
                    await session.ReadAsync("manifests", platformDigest, platformDigest, cancellationToken);
                platformManifest = JsonDocument.Parse(platformBytes);
                root = platformManifest.RootElement;
            }

            if (!root.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object ||
                !config.TryGetProperty("digest", out var configValue) ||
                configValue.ValueKind != JsonValueKind.String || !ImageDigest.IsValid(configValue.GetString()))
                throw new InvalidOperationException("The image registry returned an invalid config digest");
            var configDigest = configValue.GetString()!;
            var (configBytes, _) = await session.ReadAsync("blobs", configDigest, configDigest, cancellationToken);
            using var configJson = JsonDocument.Parse(configBytes);
            var created = ReadCreated(configJson.RootElement, "created");
            if (created is not null) return created;
            if (configJson.RootElement.TryGetProperty("config", out var imageConfig) &&
                imageConfig.ValueKind == JsonValueKind.Object &&
                imageConfig.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
                return ReadCreated(labels, "org.opencontainers.image.created");
            return null;
        }
        finally { platformManifest?.Dispose(); }
    }

    private static DateTimeOffset? ReadCreated(JsonElement element, string property) {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        if (value.TryGetDateTimeOffset(out var created)) return created;
        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out created)
            ? created
            : null;
    }

    private static string? GetPlatformDigest(JsonElement manifest, ContainerImageInfo image) {
        if (!manifest.TryGetProperty("manifests", out var manifests)) return null;
        foreach (var entry in manifests.EnumerateArray()) {
            if (!entry.TryGetProperty("platform", out var platform)) continue;
            if (!platform.TryGetProperty("os", out var os) || os.GetString() != image.Os ||
                !platform.TryGetProperty("architecture", out var architecture) ||
                architecture.GetString() != image.Architecture)
                continue;
            var variant = platform.TryGetProperty("variant", out var variantValue) ? variantValue.GetString() : null;
            if (!string.IsNullOrEmpty(image.Variant) && variant != image.Variant) continue;
            var platformDigest = entry.GetProperty("digest").GetString();
            if (!ImageDigest.IsValid(platformDigest))
                throw new InvalidOperationException("The image registry returned an invalid platform digest");
            return platformDigest;
        }

        throw new InvalidOperationException("This image tag does not support the running container platform");
    }

    private sealed class RegistrySession(HttpClient client, string registry, string repository) {
        private string? _token;

        public async Task<(byte[] Bytes, string Digest)> ReadAsync(string kind, string reference,
            string? expectedDigest, CancellationToken cancellationToken) {
            var uri = new Uri($"https://{registry}/v2/{repository}/{kind}/{Uri.EscapeDataString(reference)}");
            using var first = await SendAsync(uri, cancellationToken);
            HttpResponseMessage? authenticated = null;
            try {
                var response = first;
                if (first.StatusCode == HttpStatusCode.Unauthorized) {
                    _token = await GetTokenAsync(first, cancellationToken);
                    authenticated = await SendAsync(uri, cancellationToken);
                    response = authenticated;
                }

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"The image registry could not read image metadata (HTTP {(int)response.StatusCode})");
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (expectedDigest is not null && digest != expectedDigest) {
                    throw new InvalidOperationException(
                        "The image registry returned inconsistent image metadata digest");
                }

                if (response.Headers.TryGetValues("Docker-Content-Digest", out var headers)) {
                    var declared = headers.FirstOrDefault();
                    if (!ImageDigest.IsValid(declared) || declared != digest) {
                        throw new InvalidOperationException(
                            "The image registry returned an inconsistent manifest digest");
                    }
                }

                return (bytes, digest);
            }
            finally { authenticated?.Dispose(); }
        }

        private async Task<string> GetTokenAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
            var challenge = response.Headers.WwwAuthenticate.FirstOrDefault(x =>
                x.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));
            if (challenge?.Parameter is null)
                throw new InvalidOperationException("The image registry requires authentication that is not available");
            var values = Regex.Matches(challenge.Parameter, "([a-zA-Z]+)=\"([^\"]*)\"")
                .ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value, StringComparer.OrdinalIgnoreCase);
            if (!values.TryGetValue("realm", out var realm) ||
                !Uri.TryCreate(realm, UriKind.Absolute, out var tokenUri) ||
                tokenUri.Scheme != "https" || !string.IsNullOrEmpty(tokenUri.UserInfo))
                throw new InvalidOperationException("The image registry returned an invalid authentication challenge");
            var query = "scope=" + Uri.EscapeDataString($"repository:{repository}:pull");
            if (values.TryGetValue("service", out var service))
                query += "&service=" + Uri.EscapeDataString(service);
            var builder = new UriBuilder(tokenUri)
                { Query = string.IsNullOrEmpty(tokenUri.Query) ? query : tokenUri.Query[1..] + "&" + query };
            using var tokenResponse = await client.GetAsync(builder.Uri, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
                throw new InvalidOperationException("The image registry did not allow anonymous image access");
            using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync(cancellationToken));
            var token = tokenJson.RootElement.TryGetProperty("token", out var tokenValue) ? tokenValue.GetString() :
                tokenJson.RootElement.TryGetProperty("access_token", out tokenValue) ? tokenValue.GetString() : null;
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("The image registry returned no access token");
            return token;
        }

        private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken) {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd(Accept);
            if (_token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return await client.SendAsync(request, cancellationToken);
        }
    }
}