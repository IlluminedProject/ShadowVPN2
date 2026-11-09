using ShadowVPN2.Infrastructure.Containers.Registry.Models;
using Xunit;

namespace ShadowVPN2.Infrastructure.Containers.Tests;

public sealed class ImageReferenceTests {
    [Theory]
    [InlineData("ubuntu", "registry-1.docker.io", "library/ubuntu", "latest")]
    [InlineData("ubuntu:24.04", "registry-1.docker.io", "library/ubuntu", "24.04")]
    [InlineData("docker.io/ubuntu:24.04", "registry-1.docker.io", "library/ubuntu", "24.04")]
    [InlineData("registry-1.docker.io/org/app", "registry-1.docker.io", "org/app", "latest")]
    [InlineData("org/app:stable", "registry-1.docker.io", "org/app", "stable")]
    [InlineData("ghcr.io/org/app:stable", "ghcr.io", "org/app", "stable")]
    [InlineData("localhost:5000/org/app:stable", "localhost:5000", "org/app", "stable")]
    [InlineData("localhost/app", "localhost", "app", "latest")]
    public void Parse_resolves_registry_repository_and_tag(string reference, string registry, string repository,
        string tag) {
        Assert.Equal(new ImageReference(registry, repository, tag), ImageReference.Parse(reference));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ghcr.io/")]
    [InlineData("ghcr.io/org/app:")]
    [InlineData("https://ghcr.io/org/app")]
    [InlineData("ghcr.io/org/app@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Parse_rejects_invalid_or_digest_pinned_update_reference(string reference) {
        Assert.Throws<InvalidOperationException>(() => ImageReference.Parse(reference));
    }
}