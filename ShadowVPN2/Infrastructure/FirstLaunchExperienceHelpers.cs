using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.ServerWide.Operations;
using Serilog;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Infrastructure.Configurations;
using TruePath.SystemIo;
using ILogger = Serilog.ILogger;

namespace ShadowVPN2.Infrastructure;

public static class FirstLaunchExperienceHelpers {
    private static readonly ILogger _logger = Log.ForContext(typeof(FirstLaunchExperienceHelpers));

    public static bool IsFirstLaunch() {
        return !LocalConfiguration.Path.ExistsDirectory()
               || !LocalConfiguration.CertificatePfxPath.ExistsFile()
               || !LocalConfiguration.CertificatePemPath.ExistsFile()
               || !LocalConfiguration.ConfigPath.ExistsFile();
    }

    public static async Task InitializeFirstNode() {
        _logger.Information("Local configuration or certificates not found. Starting first-run setup");

        var localConfiguration = new LocalConfiguration {
            NodeNumber = 1
        };

        var (privateKey, _) = AwgKeyGenerator.GenerateKeyPair();
        localConfiguration.AwgPrivateKey = privateKey;
        localConfiguration.Save();

        _logger.Information("Generating new certificates for RavenDB cluster");
        var rootCa = RavenDbCertificates.GenerateRootCa();
        await LocalConfiguration.CertificatePemPath.WriteAllTextAsync(rootCa.ExportCertificatePem());

        var rootCaPfxBytes = rootCa.Export(X509ContentType.Pfx);
        await LocalConfiguration.RootCaPfxPath.WriteAllBytesAsync(rootCaPfxBytes);

        var (rsa, request) = RavenDbCertificates.GenerateCertificateRequest();
        var intermediateCertificate = RavenDbCertificates.SignCertificate(request, rootCa);
        var intermediateCertificateBytes = intermediateCertificate.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx);
        await LocalConfiguration.CertificatePfxPath.WriteAllBytesAsync(intermediateCertificateBytes);
        _logger.Information("Certificates generated and saved successfully");
    }

    public static async Task<(JoinToken Token, ClusterSignJoinResponse Response, string ConnectedTo)>
        InitializeFromJoinToken(string joinToken,
            IOptions<SingBoxOptions> singBoxOptions, SingBoxProcessManager manager) {
        var tokenJson = Encoding.UTF8.GetString(Convert.FromBase64String(joinToken));
        var token = JsonSerializer.Deserialize<JoinToken>(tokenJson, DataUtils.DefaultSerializerOptions)
                    ?? throw new Exception("Failed to deserialize join token");

        _logger.Information("Joining cluster as node {Name} with {NodeCount} seed addresses",
            token.Name, token.NodeAddresses.Count);

        // Trust the root CA from the token before connecting to existing nodes
        var rootCaCert = X509Certificate2.CreateFromPem(token.RootCaCertPem);
        RavenDbCertificates.TrustCustomRootCa(rootCaCert);
        _logger.Information("Trusted cluster Root CA from join token");

        // Generate AWG keypair locally
        var (awgPrivateKey, awgPublicKey) = AwgKeyGenerator.GenerateKeyPair();

        using var httpClient = CreateHttpClient();
        var (clusterJoinDetails, nodeRsa) = await ExchangeToken(token, awgPublicKey, httpClient);
        using (nodeRsa) {
            await BootstrapSingBox(token, clusterJoinDetails, awgPrivateKey, singBoxOptions, manager);
            var connectedTo = await ConnectToAnyPeer(clusterJoinDetails);
            await SaveJoinedNodeConfiguration(token, clusterJoinDetails, nodeRsa, awgPrivateKey);
            return (token, clusterJoinDetails, connectedTo);
        }
    }

    private static HttpClient CreateHttpClient() {
        var handler = new HttpClientHandler {
            ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
                errors is SslPolicyErrors.None
                    or SslPolicyErrors.RemoteCertificateNameMismatch
        };

        // ReSharper disable once ShortLivedHttpClient
        var httpClient = new HttpClient(handler);
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        return httpClient;
    }

    private static async Task<(ClusterSignJoinResponse Response, RSA Rsa)> ExchangeToken(JoinToken token,
        string awgPublicKey,
        HttpClient httpClient) {
        // Generate CSR for RavenDB certificate
        var (rsa, csrRequest) = RavenDbCertificates.GenerateCertificateRequest();
        var csrPem = csrRequest.CreateSigningRequestPem(
            X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1));

        var joinRequest = new ClusterSignJoinRequest {
            Secret = token.Secret,
            CsrPem = csrPem,
            AwgPublicKey = awgPublicKey
        };

        // Try each node address
        ClusterSignJoinResponse? response = null;
        foreach (var nodeAddress in token.NodeAddresses) {
            try {
                var url = $"https://{nodeAddress}/api/cluster/exchange-token";
                _logger.Information("Trying to join cluster via {Url}", url);
                var httpResponse = await httpClient.PostAsJsonAsync(url, joinRequest);
                httpResponse.EnsureSuccessStatusCode();
                response = await httpResponse.Content.ReadFromJsonAsync<ClusterSignJoinResponse>();
                _logger.Information("Successfully joined cluster via {NodeAddress}", nodeAddress);
                break;
            }
            catch (Exception ex) {
                _logger.Warning(ex, "Failed to join via {NodeAddress}", nodeAddress);
            }
        }

        if (response == null)
            throw new Exception("Failed to join cluster: all seed nodes unreachable");

        return (response, rsa);
    }

    private static async Task SaveJoinedNodeConfiguration(JoinToken token, ClusterSignJoinResponse response, RSA rsa,
        string awgPrivateKey) {
        var localConfiguration = new LocalConfiguration {
            NodeId = token.NodeId,
            AwgPrivateKey = awgPrivateKey,
            NodeNumber = response.NodeNumber
        };

        var signedCertificate = X509Certificate2.CreateFromPem(response.SignedCertPem).CopyWithPrivateKey(rsa);
        await LocalConfiguration.CertificatePfxPath.WriteAllBytesAsync(
            signedCertificate.Export(X509ContentType.Pfx));
        await LocalConfiguration.CertificatePemPath.WriteAllTextAsync(token.RootCaCertPem);
        await LocalConfiguration.RootCaPfxPath.WriteAllBytesAsync(
            X509Certificate2.CreateFromPem(token.RootCaCertPem).Export(X509ContentType.Pfx));
        localConfiguration.Save();

        _logger.Information("Joined node credentials saved successfully");
    }

    public static async Task CompleteJoinAsync(JoinToken token, ClusterSignJoinResponse response,
        string connectedTo, IDocumentStore documentStore) {
        using var httpClient = CreateHttpClient();
        await FinishJoin(token, connectedTo, httpClient);

        var tag = $"{token.Name}-{response.NodeNumber}";
        await WaitRavenDbReplication(documentStore, tag);
        _logger.Information("Node successfully joined cluster. Rebooting to complete setup");
        Environment.Exit(0);
    }

    private static async Task BootstrapSingBox(JoinToken token, ClusterSignJoinResponse response,
        string awgPrivateKey,
        IOptions<SingBoxOptions> singBoxOptions, SingBoxProcessManager manager) {
        _logger.Information("Starting bootstrap sing-box for initial cluster connectivity");

        var config = new SingBoxConfig();
        var awgSettings = response.AwgSettings;
        var nodeIp = $"100.64.0.{response.NodeNumber + 10}";

        var endpoint = new AwgEndpointConfig {
            Tag = "awg-mesh-bootstrap",
            UseIntegratedTun = singBoxOptions.Value.Awg.UseIntegratedTun,
            Address = [$"{nodeIp}/24"],
            PrivateKey = awgPrivateKey,
            ListenPort = awgSettings.ListenPort,
            Jc = awgSettings.Jc,
            Jmin = awgSettings.Jmin,
            Jmax = awgSettings.Jmax,
            S1 = awgSettings.S1,
            S2 = awgSettings.S2,
            S3 = awgSettings.S3,
            S4 = awgSettings.S4,
            H1 = awgSettings.H1,
            H2 = awgSettings.H2,
            H3 = awgSettings.H3,
            H4 = awgSettings.H4,
            I1 = awgSettings.I1,
            I2 = awgSettings.I2,
            I3 = awgSettings.I3,
            I4 = awgSettings.I4,
            I5 = awgSettings.I5
        };

        foreach (var peerInfo in response.AwgPeers) {
            var peerAddress = peerInfo.PublicAddress;
            if (string.IsNullOrEmpty(peerAddress.Host.Value)) {
                peerAddress = token.NodeAddresses
                    .FirstOrDefault(address => !string.IsNullOrEmpty(address.Host.Value));
            }

            var peer = new WireGuardPeer {
                PublicKey = peerInfo.PublicKey,
                AllowedIps = [$"{peerInfo.MeshIp}/32"],
                Address = peerAddress.Host.Value,
                Port = awgSettings.ListenPort,
                PersistentKeepaliveInterval = 25
            };

            endpoint.Peers.Add(peer);
        }

        config.Endpoints.Add(endpoint);

        config.Outbounds.Add(new DirectOutboundConfig {
            Tag = "direct"
        });

        config.Log.Level = "debug";

        var serializedConfig = JsonSerializer.Serialize(config, SingBoxService.SerializerOptions);
        await manager.ApplyConfigAsync(serializedConfig);
        manager.Start();
    }

    private static async Task<string> ConnectToAnyPeer(ClusterSignJoinResponse response) {
        _logger.Information("Waiting for connectivity to cluster nodes...");

        // Try to connect to at least one peer's RavenDB port
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        const int port = 8888;

        try {
            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
            while (!cts.IsCancellationRequested) {
                foreach (var peer in response.AwgPeers) {
                    cts.Token.ThrowIfCancellationRequested();
                    _logger.Information("Trying to connect to {PeerIp}:{Port} ({PublicAddress})",
                        peer.MeshIp, port, peer.PublicAddress);
                    using var tcpClient = new TcpClient();
                    using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    attemptCts.CancelAfter(TimeSpan.FromSeconds(5));
                    try {
                        await tcpClient.ConnectAsync(peer.MeshIp, port, attemptCts.Token);
                        _logger.Information("Connectivity verified to peer {PeerIp}:{Port} ({PublicAddress})",
                            peer.MeshIp, port, peer.PublicAddress);
                        return peer.PublicAddress.ToString();
                    }
                    catch (SocketException ex) {
                        _logger.Warning(ex,
                            "Failed to connect to cluster peer {PeerIp}:{Port} ({PublicAddress}): {SocketError}",
                            peer.MeshIp, port, peer.PublicAddress, ex.SocketErrorCode);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) {
                        _logger.Warning(
                            "Connection to cluster peer {PeerIp}:{Port} ({PublicAddress}) canceled by overall connectivity timeout",
                            peer.MeshIp, port, peer.PublicAddress);
                        throw;
                    }
                    catch (OperationCanceledException) when (attemptCts.IsCancellationRequested) {
                        _logger.Warning(
                            "Connection to cluster peer {PeerIp}:{Port} ({PublicAddress}) timed out after 5 seconds",
                            peer.MeshIp, port, peer.PublicAddress);
                    }
                }

                await Task.Delay(1000, cts.Token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            // Both connection attempts and retry delays share the overall timeout.
        }

        _logger.Error("Failed to verify connectivity to any cluster peer on TCP port {Port} within 120 seconds", port);
        throw new TimeoutException(
            "Failed to verify connectivity to any cluster peer on TCP port 8888 within 120 seconds");
    }

    private static async Task FinishJoin(JoinToken token, string nodeAddress, HttpClient httpClient) {
        var baseUrl = nodeAddress.Contains("://", StringComparison.Ordinal)
            ? nodeAddress
            : $"https://{nodeAddress}";
        var url = $"{baseUrl.TrimEnd('/')}/api/cluster/finish-join";
        _logger.Information("Trying to finish joining the cluster via {Url}", url);
        var finishJoinRequest = new ClusterFinishJoinRequest(token.Secret);
        var httpResponse = await httpClient.PostAsJsonAsync(url, finishJoinRequest);
        httpResponse.EnsureSuccessStatusCode();

        _logger.Information("Successfully finished joining the cluster via {Url}", url);
    }

    private static async Task WaitRavenDbReplication(IDocumentStore store, string nodeTag) {
        _logger.Information(
            "Waiting for RavenDB cluster to fully replicate to this node. If this takes too long, check the connection");
        while (true) {
            var record = await store.Maintenance.Server.SendAsync(
                new GetDatabaseRecordOperation(store.Database));

            var replicated = record.Topology.Members.Contains(nodeTag) &&
                             !record.Topology.Promotables.Contains(nodeTag);

            if (replicated) {
                _logger.Information("RavenDB cluster is fully replicated, node is ready");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}