using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Infrastructure.Containers.Registry;
using ShadowVPN2.Infrastructure.Containers.Registry.Models;
using Xunit;

namespace ShadowVPN2.Infrastructure.Containers.Tests;

public sealed class RegistryImageClientTests {
    private static readonly ContainerImageInfo Platform = new() { Os = "linux", Architecture = "amd64" };

    [Theory]
    [InlineData(
        "{\"created\":\"2026-10-15T12:30:00Z\",\"config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2025-01-01T00:00:00Z\"}}}",
        "2026-10-15T12:30:00Z")]
    [InlineData("{\"created\":\"2026-11-08T01:49:15.56850731Z\"}", "2026-11-08T01:49:15.5685073Z")]
    [InlineData("{\"config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2026-10-15T15:30:00+03:00\"}}}",
        "2026-10-15T12:30:00Z")]
    [InlineData(
        "{\"created\":\"invalid\",\"config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2026-10-15T12:30:00Z\"}}}",
        "2026-10-15T12:30:00Z")]
    [InlineData(
        "{\"created\":123,\"config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2026-10-15T12:30:00Z\"}}}",
        "2026-10-15T12:30:00Z")]
    [InlineData("{}", null)]
    [InlineData("{\"created\":null}", null)]
    [InlineData(
        "{\"created\":{},\"config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2026-10-15T12:30:00Z\"}}}",
        "2026-10-15T12:30:00Z")]
    [InlineData(
        "{\"created\":[\"invalid\"],\"config\":{\"Labels\":{\"org.opencontainers.image.created\":\"2026-10-15T12:30:00Z\"}}}",
        "2026-10-15T12:30:00Z")]
    [InlineData("{\"config\":{\"Labels\":{\"org.opencontainers.image.created\":{}}}}", null)]
    [InlineData(
        "{\"created\":\"invalid\",\"config\":{\"Labels\":{\"org.opencontainers.image.created\":\"also invalid\"}}}",
        null)]
    [InlineData("{\"config\":{\"Labels\":{\"org.opencontainers.image.created\":123}}}", null)]
    public async Task GetCreatedAsync_reads_build_timestamp_from_config(string config, string? expected) {
        var manifest = Manifest(Digest(config));
        var requests = new List<string>();
        using var factory = new RegistryFactory(request => {
            requests.Add(Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
            return request.RequestUri.AbsolutePath.Contains("/blobs/", StringComparison.Ordinal)
                ? Json(config)
                : Json(manifest);
        });

        var created = await new RegistryImageClient(factory).GetCreatedAsync(
            ImageReference.Parse("registry.example/app"), Digest(manifest), Platform, CancellationToken.None);

        Assert.Equal(expected is null ? null : DateTimeOffset.Parse(expected), created);
        Assert.Equal(new[] { $"/v2/app/manifests/{Digest(manifest)}", $"/v2/app/blobs/{Digest(config)}" }, requests);
    }

    [Theory]
    [InlineData("application/vnd.oci.image.index.v1+json")]
    [InlineData("application/vnd.docker.distribution.manifest.list.v2+json")]
    public async Task GetCreatedAsync_selects_platform_and_variant_and_reuses_bearer_for_config(string mediaType) {
        const string config = "{\"created\":\"2026-10-15T12:30:00Z\"}";
        var manifest = Manifest(Digest(config));
        var index = JsonSerializer.Serialize(new {
            schemaVersion = 2,
            mediaType,
            manifests = new[] {
                new {
                    digest = "sha256:" + new string('a', 64),
                    platform = new { os = "windows", architecture = "arm64", variant = "v8" }
                },
                new {
                    digest = "sha256:" + new string('b', 64),
                    platform = new { os = "linux", architecture = "amd64", variant = "v8" }
                },
                new {
                    digest = "sha256:" + new string('c', 64),
                    platform = new { os = "linux", architecture = "arm64", variant = "v7" }
                },
                new {
                    digest = Digest(manifest), platform = new { os = "linux", architecture = "arm64", variant = "v8" }
                }
            }
        });
        var requests = new List<string>();
        var tokenRequests = 0;
        using var factory = new RegistryFactory(request => {
            var uri = request.RequestUri!;
            if (uri.Host == "auth.example") {
                tokenRequests++;
                Assert.Null(request.Headers.Authorization);
                Assert.Contains("scope=repository%3Aorg%2Fapp%3Apull", uri.Query);
                Assert.Contains("service=registry.example", uri.Query);
                return Json("{\"access_token\":\"registry-token\"}");
            }

            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            requests.Add(path);
            if (request.Headers.Authorization is null) {
                Assert.Equal($"/v2/org/app/manifests/{Digest(index)}", path);
                var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                unauthorized.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer",
                    "realm=\"https://auth.example/token\",service=\"registry.example\""));
                return unauthorized;
            }

            Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
            Assert.Equal("registry-token", request.Headers.Authorization.Parameter);
            if (path == $"/v2/org/app/manifests/{Digest(index)}") return Json(index);
            if (path == $"/v2/org/app/manifests/{Digest(manifest)}") return Json(manifest);
            Assert.Equal($"/v2/org/app/blobs/{Digest(config)}", path);
            return Json(config);
        });

        var created = await new RegistryImageClient(factory).GetCreatedAsync(
            ImageReference.Parse("registry.example/org/app"), Digest(index),
            new ContainerImageInfo { Os = "linux", Architecture = "arm64", Variant = "v8" }, CancellationToken.None);

        Assert.Equal(DateTimeOffset.Parse("2026-10-15T12:30:00Z"), created);
        Assert.Equal(1, tokenRequests);
        Assert.Equal(new[] {
            $"/v2/org/app/manifests/{Digest(index)}", $"/v2/org/app/manifests/{Digest(index)}",
            $"/v2/org/app/manifests/{Digest(manifest)}", $"/v2/org/app/blobs/{Digest(config)}"
        }, requests);
    }

    [Fact]
    public async Task GetCreatedAsync_authenticates_when_only_config_requires_bearer() {
        const string config = "{\"created\":\"2026-10-15T12:30:00Z\"}";
        var manifest = Manifest(Digest(config));
        var configRequests = 0;
        using var factory = new RegistryFactory(request => {
            if (request.RequestUri!.Host == "auth.example") return Json("{\"token\":\"config-token\"}");
            if (request.RequestUri.AbsolutePath.Contains("/manifests/", StringComparison.Ordinal))
                return Json(manifest);
            configRequests++;
            if (request.Headers.Authorization is null) {
                var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                unauthorized.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer",
                    "realm=\"https://auth.example/token\""));
                return unauthorized;
            }

            Assert.Equal("config-token", request.Headers.Authorization.Parameter);
            return Json(config);
        });

        var created = await new RegistryImageClient(factory).GetCreatedAsync(
            ImageReference.Parse("registry.example/app"), Digest(manifest), Platform, CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal(2, configRequests);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("platform")]
    [InlineData("config")]
    public async Task GetCreatedAsync_rejects_bytes_not_matching_requested_digest(string corruptedPart) {
        const string config = "{\"created\":\"2026-10-15T12:30:00Z\"}";
        var manifest = Manifest(Digest(config));
        var index = JsonSerializer.Serialize(new {
            manifests = new[]
                { new { digest = Digest(manifest), platform = new { os = "linux", architecture = "amd64" } } }
        });
        using var factory = new RegistryFactory(request => {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            if (path.EndsWith(Digest(index), StringComparison.Ordinal))
                return Json(corruptedPart == "manifest" ? index + " " : index);
            if (path.EndsWith(Digest(manifest), StringComparison.Ordinal))
                return Json(corruptedPart == "platform" ? manifest + " " : manifest);
            Assert.EndsWith(Digest(config), path);
            return Json(corruptedPart == "config" ? config + " " : config);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RegistryImageClient(factory).GetCreatedAsync(
            ImageReference.Parse("registry.example/app"), Digest(index), Platform, CancellationToken.None));
    }

    [Fact]
    public async Task GetCreatedAsync_rejects_inconsistent_declared_digest() {
        var manifest = Manifest("sha256:" + new string('a', 64));
        using var factory = new RegistryFactory(_ => {
            var response = Json(manifest);
            response.Headers.Add("Docker-Content-Digest", "sha256:" + new string('b', 64));
            return response;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RegistryImageClient(factory).GetCreatedAsync(
            ImageReference.Parse("registry.example/app"), Digest(manifest), Platform, CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"config\":{\"digest\":\"sha256:abc\"}}")]
    [InlineData("{\"config\":{\"digest\":123}}")]
    [InlineData("{}")]
    public async Task GetCreatedAsync_rejects_invalid_config_descriptor_before_fetching_blob(string manifest) {
        var requests = 0;
        using var factory = new RegistryFactory(_ => {
            requests++;
            return Json(manifest);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RegistryImageClient(factory).GetCreatedAsync(
            ImageReference.Parse("registry.example/app"), Digest(manifest), Platform, CancellationToken.None));

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task GetCreatedAsync_rejects_index_without_running_platform() {
        var index = JsonSerializer.Serialize(new {
            manifests = new[] {
                new {
                    digest = "sha256:" + new string('a', 64), platform = new { os = "linux", architecture = "arm64" }
                }
            }
        });
        var requests = 0;
        using var factory = new RegistryFactory(_ => {
            requests++;
            return Json(index);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RegistryImageClient(factory).GetCreatedAsync(
            ImageReference.Parse("registry.example/app"), Digest(index), Platform, CancellationToken.None));

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task GetDigestAsync_preserves_tag_and_platform_digest_resolution() {
        var platformDigest = "sha256:" + new string('a', 64);
        var index = JsonSerializer.Serialize(new {
            manifests = new[]
                { new { digest = platformDigest, platform = new { os = "linux", architecture = "amd64" } } }
        });
        using var factory = new RegistryFactory(request => {
            Assert.Equal("/v2/app/manifests/stable", request.RequestUri!.AbsolutePath);
            var response = Json(index);
            response.Headers.Add("Docker-Content-Digest", Digest(index));
            return response;
        });

        var result = await new RegistryImageClient(factory).GetDigestAsync(
            ImageReference.Parse("registry.example/app:stable"), Platform, CancellationToken.None);

        Assert.Equal(Digest(index), result.Digest);
        Assert.Equal(platformDigest, result.PlatformDigest);
    }

    private static string Manifest(string configDigest) {
        return JsonSerializer.Serialize(new {
            schemaVersion = 2,
            mediaType = "application/vnd.oci.image.manifest.v1+json",
            config = new { digest = configDigest },
            layers = new[] { new { digest = "sha256:" + new string('f', 64) } }
        });
    }

    private static string Digest(string json) {
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static HttpResponseMessage Json(string json) {
        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RegistryFactory(Func<HttpRequestMessage, HttpResponseMessage> handle)
        : IHttpClientFactory, IDisposable {
        private readonly RegistryHandler _handler = new(handle);

        public void Dispose() {
            _handler.Dispose();
        }

        public HttpClient CreateClient(string name) {
            return new HttpClient(_handler, false);
        }
    }

    private sealed class RegistryHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) {
            return Task.FromResult(handle(request));
        }
    }
}