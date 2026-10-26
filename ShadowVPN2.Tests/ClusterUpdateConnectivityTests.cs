using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Raven.Client.Documents;
using Raven.Client.Http;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Exceptions;
using ShadowVPN2.Infrastructure.RavenDb.RavenEmbedded;
using Xunit;

namespace ShadowVPN2.Tests;

public class ClusterUpdateConnectivityTests {
    [Fact]
    public async Task Unreachable_leader_rejects_start_before_accessing_update_state() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var leaderUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        listener.Stop();
        using var store = new DocumentStore { Urls = ["http://127.0.0.1:1"], Database = "test" };
        store.Conventions.DisableTopologyUpdates = true;
        store.Initialize();
        var topology = new ClusterTopologyResponse {
            Leader = "A", NodeTag = "B",
            Topology = new ClusterTopology {
                Members = new Dictionary<string, string> { ["A"] = leaderUrl, ["B"] = store.Urls[0] }
            }
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var error = await Assert.ThrowsAsync<AppException>(() => new ClusterUpdateService(store).StartAsync(
            new StartClusterUpdateRequest {
                BackwardCompatible = true, ImageDigest = "sha256:" + new string('a', 64)
            }, topology, "B", timeout.Token));
        Assert.Equal(409, error.StatusCode);
        Assert.Contains("leader 'A' is unreachable", error.Message);
    }

    [Fact]
    public async Task Follower_checks_leader_connectivity_with_topology_updates_disabled() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = """
            {"Leader":"A","NodeTag":"A","Topology":{"TopologyId":"test","Members":{"A":"URL","B":"http://localhost:1","C":"http://localhost:2"},"Promotables":{},"Watchers":{},"LastNodeId":"C","Etag":1},"Etag":1,"Status":{"B":{"Connected":true},"C":{"Connected":false}}}
            """.Replace("URL", url);
        var server = Task.Run(async () => {
            while (!timeout.IsCancellationRequested) {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
                var body = Encoding.UTF8.GetBytes(response);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
            }
        });
        try {
            using var store = new DocumentStore { Urls = [url], Database = "test" };
            store.Conventions.DisableTopologyUpdates = true;
            store.Initialize();
            var topology = new ClusterTopologyResponse {
                Leader = "A", NodeTag = "B",
                Topology = new ClusterTopology {
                    Members = new Dictionary<string, string> {
                        ["A"] = url, ["B"] = "http://localhost:1", ["C"] = "http://localhost:2"
                    }
                }
            };
            var method = typeof(ClusterUpdateService).GetMethod("GetConnectedMembersAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (Task<int>)method.Invoke(new ClusterUpdateService(store), [topology, timeout.Token])!;
            Assert.Equal(2, await result);
            Assert.True(store.Conventions.DisableTopologyUpdates);
        }
        finally {
            await timeout.CancelAsync();
            listener.Stop();
            try { await server; }
            catch (OperationCanceledException) { }
            catch (SocketException) when (timeout.IsCancellationRequested) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Leader_probe_preserves_shared_certificate_after_success_or_failure(bool failProbe) {
        using var certificate = CreateCertificate("client");
        using var peerCertificate = CreateCertificate("localhost");
        var thumbprint = certificate.Thumbprint;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var url = $"https://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = failProbe
            ? """{"Type":"System.InvalidOperationException","Message":"Probe rejected","Error":"Probe rejected"}"""
            : """
              {"Leader":"A","NodeTag":"A","Topology":{"TopologyId":"test","Members":{"A":"URL","B":"https://localhost:1"},"Promotables":{},"Watchers":{},"LastNodeId":"B","Etag":1},"Etag":1,"Status":{"B":{"Connected":true}}}
              """.Replace("URL", url);
        var server = Task.Run(async () => {
            while (!timeout.IsCancellationRequested) {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = new SslStream(client.GetStream());
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                    ServerCertificate = peerCertificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, timeout.Token);
                using var reader = new StreamReader(stream, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
                var body = Encoding.UTF8.GetBytes(response);
                var status = failProbe ? "500 Internal Server Error" : "200 OK";
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
            }
        });
        try {
            using var store = new DocumentStore { Urls = [url], Database = "test", Certificate = certificate };
            store.Conventions.DisableTopologyUpdates = true;
            var validatedPeer = false;
            store.Conventions.CreateHttpClient = handler => {
                handler.ServerCertificateCustomValidationCallback = (sender, peer, _, errors) => {
                    // Use a private trust store so this exercises trusted peer TLS without changing OS roots.
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(peerCertificate);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    if (peer != null && chain.Build(peer))
                        errors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
                    var accepted = ValidateServerCertificate(sender, peer, errors, thumbprint);
                    validatedPeer |= accepted;
                    return accepted;
                };
                return new HttpClient(handler);
            };
            store.Initialize();
            var topology = new ClusterTopologyResponse {
                Leader = "A", NodeTag = "B",
                Topology = new ClusterTopology {
                    Members = new Dictionary<string, string> { ["A"] = url, ["B"] = "https://localhost:1" }
                }
            };
            var method = typeof(ClusterUpdateService).GetMethod("GetConnectedMembersAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var probe = (Task<int>)method.Invoke(new ClusterUpdateService(store), [topology, timeout.Token])!;
            if (failProbe) {
                var error = await Assert.ThrowsAnyAsync<Exception>(() => probe);
                Assert.IsNotType<OperationCanceledException>(error);
            }
            else {
                Assert.Equal(2, await probe);
            }

            Assert.True(validatedPeer);
            Assert.False(timeout.IsCancellationRequested);
            Assert.Same(certificate, store.Certificate);
            Assert.Equal(thumbprint, store.Certificate.Thumbprint);
            using var privateKey = store.Certificate.GetRSAPrivateKey();
            Assert.NotNull(privateKey);
            var data = Encoding.UTF8.GetBytes("certificate remains usable after leader store disposal");
            var signature = privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var publicKey = store.Certificate.GetRSAPublicKey();
            Assert.True(publicKey!.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        finally {
            await timeout.CancelAsync();
            listener.Stop();
            try { await server; }
            catch (OperationCanceledException) { }
            catch (SocketException) when (timeout.IsCancellationRequested) { }
        }
    }

    [Theory]
    [InlineData(SslPolicyErrors.None, true)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable, false)]
    public void Remote_peer_requires_platform_trust_and_matching_hostname(SslPolicyErrors errors, bool expected) {
        using var certificate = CreateCertificate("peer");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://100.64.0.11:8888");
        // Even presenting the local certificate cannot bypass hostname validation for a remote target.
        Assert.Equal(expected, ValidateServerCertificate(request, certificate, errors, certificate.Thumbprint));
    }

    [Theory]
    [InlineData("https://127.0.0.1:8888", true)]
    [InlineData("https://localhost:8888", true)]
    [InlineData("https://[::1]:8888", true)]
    [InlineData("https://100.64.0.11:8888", false)]
    public void Local_certificate_pin_only_bypasses_errors_for_loopback(string url, bool expected) {
        using var certificate = CreateCertificate("mesh");
        using var otherCertificate = CreateCertificate("other");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var errors = SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors;
        Assert.Equal(expected, ValidateServerCertificate(request, certificate, errors, certificate.Thumbprint));
        Assert.False(ValidateServerCertificate(request, otherCertificate, errors, certificate.Thumbprint));
        Assert.False(ValidateServerCertificate(new object(), certificate, errors, certificate.Thumbprint));
        Assert.False(ValidateServerCertificate(request, null, SslPolicyErrors.None, certificate.Thumbprint));
    }

    private static bool ValidateServerCertificate(object sender, X509Certificate? certificate,
        SslPolicyErrors errors, string thumbprint) {
        var method = typeof(EmbeddedServer).GetMethod("ValidateServerCertificate",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, [sender, certificate, errors, thumbprint])!;
    }

    private static X509Certificate2 CreateCertificate(string name) {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(name);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection {
            new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2")
        }, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.DefaultKeySet);
    }
}
