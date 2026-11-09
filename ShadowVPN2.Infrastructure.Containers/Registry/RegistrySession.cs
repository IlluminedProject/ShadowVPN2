using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Infrastructure.Containers.Registry.Models;

namespace ShadowVPN2.Infrastructure.Containers.Registry;

internal sealed class RegistrySession(HttpClient client, ImageReference image) {
    private const string Accept =
        "application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json";

    private string? _token;

    public async Task<RegistryReadResult<T>> ReadAsync<T>(RegistryResource resource, string reference,
        string? expectedDigest, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken) where T : class {
        var kind = resource switch {
            RegistryResource.Manifest => "manifests",
            RegistryResource.Blob => "blobs",
            _ => throw new ArgumentOutOfRangeException(nameof(resource))
        };
        var uri = new Uri($"https://{image.Registry}/v2/{image.Repository}/{kind}/{Uri.EscapeDataString(reference)}");
        using var first = await SendAsync(uri, cancellationToken);
        HttpResponseMessage? authenticated = null;
        try {
            var response = first;
            if (first.StatusCode == HttpStatusCode.Unauthorized) {
                _token = await GetTokenAsync(first, cancellationToken);
                authenticated = await SendAsync(uri, cancellationToken);
                response = authenticated;
            }

            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException(
                    $"The image registry could not read image metadata (HTTP {(int)response.StatusCode})");
            }

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

            try {
                var value = JsonSerializer.Deserialize(bytes, jsonTypeInfo) ??
                            throw new InvalidOperationException("The image registry returned empty image metadata");
                return new RegistryReadResult<T>(value, digest);
            }
            catch (JsonException exception) {
                throw new InvalidOperationException("The image registry returned invalid image metadata", exception);
            }
        }
        finally { authenticated?.Dispose(); }
    }

    private async Task<string> GetTokenAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        var challenge = response.Headers.WwwAuthenticate.FirstOrDefault(x =>
            x.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));
        var authentication = RegistryAuthenticationChallenge.Parse(challenge);
        using var tokenResponse =
            await client.GetAsync(authentication.GetTokenUri(image.Repository), cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("The image registry did not allow anonymous image access");
        var tokenResponseBody = await tokenResponse.Content.ReadFromJsonAsync(
            RegistryJsonContext.Default.RegistryTokenResponse, cancellationToken);
        var token = tokenResponseBody?.Token ?? tokenResponseBody?.AccessToken;
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