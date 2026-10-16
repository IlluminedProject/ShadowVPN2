using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Models;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class NaiveProxySharedPortExtensionTests {
    [Fact]
    public async Task SharedTcpListenerIsRewrittenBeforeItsRouteIsPublished() {
        using var server = new AddressServer("https://127.0.0.1:443");
        await using var extension = new NaiveProxySharedPortExtension(server);
        var inbound = CreateInbound(443);
        var context = CreateContext(inbound);

        await extension.ConfigureAsync(context, CancellationToken.None);

        Assert.Equal("127.0.0.1", inbound.Listen);
        var backendPort = Assert.IsType<int>(inbound.ListenPort);
        Assert.InRange(backendPort, 1, 65535);
        Assert.NotEqual(443, backendPort);
        Assert.Null(inbound.Tls);
        Assert.False(extension.TryGetBackend(443, out _));
        Assert.Null(extension.GetFallbackCertificate(443));

        await extension.ApplyAsync(context, CancellationToken.None);

        Assert.True(extension.TryGetBackend(443, out var activePort));
        Assert.Equal(backendPort, activePort);
        Assert.False(extension.TryGetBackend(8443, out _));
    }

    [Fact]
    public async Task ReconfigurationReusesBackendAndKeepsAppliedRouteUntilRemovalIsApplied() {
        using var server = new AddressServer("https://127.0.0.1:443");
        await using var extension = new NaiveProxySharedPortExtension(server);
        var first = CreateContext(CreateInbound(443));
        await extension.ConfigureAsync(first, CancellationToken.None);
        await extension.ApplyAsync(first, CancellationToken.None);
        Assert.True(extension.TryGetBackend(443, out var originalBackend));

        var replacement = CreateContext(CreateInbound(443));
        await extension.ConfigureAsync(replacement, CancellationToken.None);
        Assert.Equal(originalBackend, replacement.Config.Inbounds.Single().ListenPort);
        Assert.True(extension.TryGetBackend(443, out var stillActiveBackend));
        Assert.Equal(originalBackend, stillActiveBackend);
        await extension.ApplyAsync(replacement, CancellationToken.None);

        var removed = CreateContext();
        await extension.ConfigureAsync(removed, CancellationToken.None);
        Assert.True(extension.TryGetBackend(443, out _));
        await extension.ApplyAsync(removed, CancellationToken.None);
        Assert.False(extension.TryGetBackend(443, out _));
        Assert.Null(extension.GetFallbackCertificate(443));
    }

    [Fact]
    public async Task ApplyDoesNotPublishConfiguredListenerWhoseUsersWereRemoved() {
        using var server = new AddressServer("https://127.0.0.1:443");
        await using var extension = new NaiveProxySharedPortExtension(server);
        var inbound = CreateInbound(443);
        var context = CreateContext(inbound);
        await extension.ConfigureAsync(context, CancellationToken.None);
        await extension.ApplyAsync(context, CancellationToken.None);
        Assert.True(extension.TryGetBackend(443, out _));

        inbound.Users.Clear();
        await extension.ApplyAsync(context, CancellationToken.None);

        Assert.False(extension.TryGetBackend(443, out _));
        Assert.Null(extension.GetFallbackCertificate(443));
    }

    [Fact]
    public async Task ApplyDoesNotPublishListenerRemovedAfterConfiguration() {
        using var server = new AddressServer("https://127.0.0.1:443");
        await using var extension = new NaiveProxySharedPortExtension(server);
        var context = CreateContext(CreateInbound(443));
        await extension.ConfigureAsync(context, CancellationToken.None);
        context.Config.Inbounds.Clear();

        await extension.ApplyAsync(context, CancellationToken.None);

        Assert.False(extension.TryGetBackend(443, out _));
        Assert.Null(extension.GetFallbackCertificate(443));
    }

    [Theory]
    [InlineData("https://127.0.0.1:443", 443, "udp", true)]
    [InlineData("https://127.0.0.1:443", 8443, "tcp", true)]
    [InlineData("http://127.0.0.1:443", 443, "tcp", true)]
    [InlineData("https://127.0.0.1:443", 443, "tcp", false)]
    public async Task NonsharedQuicAndEmptyListenersRemainUntouched(
        string address, int port, string network, bool hasUsers) {
        using var server = new AddressServer(address);
        await using var extension = new NaiveProxySharedPortExtension(server);
        var inbound = CreateInbound(port);
        inbound.Network = network;
        if (!hasUsers) inbound.Users.Clear();
        var originalTls = inbound.Tls;
        var context = CreateContext(inbound);

        await extension.ConfigureAsync(context, CancellationToken.None);
        await extension.ApplyAsync(context, CancellationToken.None);

        Assert.Same(inbound, Assert.Single(context.Config.Inbounds));
        Assert.Equal("0.0.0.0", inbound.Listen);
        Assert.Equal(port, inbound.ListenPort);
        Assert.Equal(network, inbound.Network);
        Assert.Same(originalTls, inbound.Tls);
        Assert.False(extension.TryGetBackend(port, out _));
        Assert.Null(extension.GetFallbackCertificate(port));
    }

    [Fact]
    public async Task FallbackCertificateComesFromOriginalTlsAndChangesOnlyOnApply() {
        using var server = new AddressServer("https://127.0.0.1:443");
        await using var extension = new NaiveProxySharedPortExtension(server);
        using var firstKey = RSA.Create(2048);
        using var firstCertificate = CreateCertificate(firstKey, "first.shadowvpn.local");
        var first = CreateContext(CreateInbound(443, CreateTls(firstCertificate, firstKey)));

        await extension.ConfigureAsync(first, CancellationToken.None);
        Assert.Null(extension.GetFallbackCertificate(443));
        await extension.ApplyAsync(first, CancellationToken.None);
        var activeCertificate = Assert.IsType<X509Certificate2>(extension.GetFallbackCertificate(443));
        Assert.True(activeCertificate.HasPrivateKey);
        Assert.Equal(firstCertificate.GetCertHash(HashAlgorithmName.SHA256),
            activeCertificate.GetCertHash(HashAlgorithmName.SHA256));
        Assert.Null(extension.GetFallbackCertificate(8443));

        using var replacementKey = RSA.Create(2048);
        using var replacementCertificate = CreateCertificate(replacementKey, "replacement.shadowvpn.local");
        var replacement = CreateContext(CreateInbound(443, CreateTls(replacementCertificate, replacementKey)));
        await extension.ConfigureAsync(replacement, CancellationToken.None);
        Assert.Same(activeCertificate, extension.GetFallbackCertificate(443));
        await extension.ApplyAsync(replacement, CancellationToken.None);
        var appliedCertificate = Assert.IsType<X509Certificate2>(extension.GetFallbackCertificate(443));
        Assert.True(appliedCertificate.HasPrivateKey);
        Assert.Equal(replacementCertificate.GetCertHash(HashAlgorithmName.SHA256),
            appliedCertificate.GetCertHash(HashAlgorithmName.SHA256));

        var removed = CreateContext();
        await extension.ConfigureAsync(removed, CancellationToken.None);
        await extension.ApplyAsync(removed, CancellationToken.None);
        Assert.Null(extension.GetFallbackCertificate(443));
    }

    [Fact]
    public async Task DisposeClearsAppliedRoutesAndFallbackCertificates() {
        using var server = new AddressServer("https://127.0.0.1:443");
        await using var extension = new NaiveProxySharedPortExtension(server);
        using var key = RSA.Create(2048);
        using var certificate = CreateCertificate(key, "shadowvpn.local");
        var context = CreateContext(CreateInbound(443, CreateTls(certificate, key)));
        await extension.ConfigureAsync(context, CancellationToken.None);
        await extension.ApplyAsync(context, CancellationToken.None);
        Assert.True(extension.TryGetBackend(443, out _));
        Assert.NotNull(extension.GetFallbackCertificate(443));

        await extension.DisposeAsync();

        Assert.False(extension.TryGetBackend(443, out _));
        Assert.Null(extension.GetFallbackCertificate(443));
    }

    private static SingBoxContext CreateContext(params InboundConfig[] inbounds) {
        var config = new SingBoxConfig();
        config.Inbounds.AddRange(inbounds);
        return new SingBoxContext(config, [], [], []);
    }

    private static NaiveProxyInboundConfig CreateInbound(int port, InboundTlsConfig? tls = null) {
        return new NaiveProxyInboundConfig {
            Tag = $"naive-{port}",
            Listen = "0.0.0.0",
            ListenPort = port,
            Users = [new NaiveProxyUser { Username = "user", Password = "secret" }],
            Tls = tls ?? CreateDefaultTls()
        };
    }

    private static InboundTlsConfig CreateDefaultTls() {
        using var key = RSA.Create(2048);
        using var certificate = CreateCertificate(key, "shadowvpn.local");
        return CreateTls(certificate, key);
    }

    private static X509Certificate2 CreateCertificate(RSA key, string name) {
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static InboundTlsConfig CreateTls(X509Certificate2 certificate, RSA key) {
        return new InboundTlsConfig {
            Enabled = true,
            Certificate = certificate.ExportCertificatePem().Split('\n').ToList(),
            Key = key.ExportPkcs8PrivateKeyPem().Split('\n').ToList()
        };
    }

    private sealed class AddressServer : IServer {
        public AddressServer(params string[] addresses) {
            var feature = new ServerAddressesFeature();
            foreach (var address in addresses) feature.Addresses.Add(address);
            Features.Set<IServerAddressesFeature>(feature);
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}