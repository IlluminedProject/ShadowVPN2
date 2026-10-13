using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.ServerWide.Operations;
using Serilog;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
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

    public static async Task InitializeFirstNode(IOptions<SingBoxOptions> singBoxOptions,
        SingBoxProcessManager manager) {
        _logger.Information("Starting first-run setup with a self-signed node certificate");
        var localConfiguration = new LocalConfiguration { NodeNumber = 1 };
        var (privateKey, _) = AwgKeyGenerator.GenerateKeyPair();
        localConfiguration.AwgPrivateKey = privateKey;
        var meshIp = $"100.64.0.{localConfiguration.NodeNumber + 10}";
        using var certificate = RavenDbCertificates.GenerateNodeCertificate(IPAddress.Parse(meshIp));
        await LocalConfiguration.CertificatePfxPath.WriteAllBytesAsync(certificate.Export(X509ContentType.Pfx));
        await LocalConfiguration.CertificatePemPath.WriteAllTextAsync(certificate.ExportCertificatePem());
        await PeerCertificateTrustService.SaveAsync([
            new NodeCertificateTrust(localConfiguration.NodeId, meshIp, certificate.ExportCertificatePem())
        ]);
        await LocalConfiguration.SetupPendingPath.WriteAllTextAsync(string.Empty);
        localConfiguration.Save();
        await BootstrapSingBox(localConfiguration.NodeNumber, new AwgGlobalSettings(), [], [],
            privateKey, singBoxOptions, manager);
        await WaitForMeshAddressAsync(IPAddress.Parse(meshIp), manager);
    }

    public static async Task<(JoinToken Token, ClusterJoinResponse Response, string ConnectedTo)>
        InitializeFromJoinToken(string joinToken,
            IOptions<SingBoxOptions> singBoxOptions, SingBoxProcessManager manager, IServiceProvider services) {
        var tokenJson = Encoding.UTF8.GetString(Convert.FromBase64String(joinToken));
        var token = JsonSerializer.Deserialize<JoinToken>(tokenJson, DataUtils.DefaultSerializerOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize join token");
        if (token.NodeNumber < 1 || token.Seeds.Count == 0) {
            throw new InvalidOperationException(
                "The join token must contain the assigned node number and seed certificate pins.");
        }

        _logger.Information("Joining cluster as node {Name} with {NodeCount} seed addresses",
            token.Name, token.Seeds.Count);
        var (awgPrivateKey, awgPublicKey) = AwgKeyGenerator.GenerateKeyPair();
        using var certificate = RavenDbCertificates.GenerateNodeCertificate(
            IPAddress.Parse($"100.64.0.{token.NodeNumber + 10}"));
        var (response, seedAddress) = await ExchangeToken(token, awgPublicKey, certificate.ExportCertificatePem());
        if (response.NodeNumber != token.NodeNumber)
            throw new InvalidOperationException("The seed returned a different node number than the join token.");

        var temporaryDirectory = LocalConfiguration.Path / $"join-{Guid.NewGuid():N}";
        temporaryDirectory.CreateDirectory();
        var temporaryCertificatePath = temporaryDirectory / "node.pfx";
        var ownTrust = new NodeCertificateTrust(token.NodeId, $"100.64.0.{token.NodeNumber + 10}",
            certificate.ExportCertificatePem());
        var peers = response.PeerCertificates.Where(peer => peer.NodeId != token.NodeId).Append(ownTrust).ToArray();
        foreach (var peer in peers)
            using (RavenDbCertificates.LoadNodeCertificate(peer.CertificatePem, IPAddress.Parse(peer.MeshIp))) { }

        try {
            await PeerCertificateTrustService.InstallRootStoreAsync(peers);
            await temporaryCertificatePath.WriteAllBytesAsync(certificate.Export(X509ContentType.Pfx));
            services.GetRequiredService<JoinStartupState>().Configure(
                temporaryCertificatePath.Value, token.NodeNumber, peers, awgPrivateKey,
                certificate.ExportCertificatePem());
            await BootstrapSingBox(response.NodeNumber, response.AwgSettings, response.AwgPeers,
                token.Seeds.Select(seed => seed.Address), awgPrivateKey, singBoxOptions, manager);
            await ConnectToAnyPeer(response);
            return (token, response, seedAddress);
        }
        catch {
            services.GetRequiredService<JoinStartupState>().Clear();
            await PeerCertificateTrustService.RestoreRootStoreAsync(peers);
            Directory.Delete(temporaryDirectory.Value, true);
            throw;
        }
    }

    private static HttpClient CreateHttpClient(JoinSeed seed) {
        var pin = X509Certificate2.CreateFromPem(seed.CertificatePem);
        var handler = new SeedHttpClientHandler(pin) {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (request, certificate, _, _) =>
                request.RequestUri!.Host.Equals(seed.Address.Host.Value, StringComparison.OrdinalIgnoreCase) &&
                RavenDbCertificates.MatchesPin(certificate, pin)
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static async Task<(ClusterJoinResponse Response, string SeedAddress)> ExchangeToken(JoinToken token,
        string awgPublicKey, string certificatePem) {
        var joinRequest = new ClusterJoinRequest {
            Secret = token.Secret,
            CertificatePem = certificatePem,
            AwgPublicKey = awgPublicKey
        };
        foreach (var seed in token.Seeds) {
            try {
                using var httpClient = CreateHttpClient(seed);
                var url = $"https://{seed.Address}/api/cluster/exchange-token";
                _logger.Information("Trying to join cluster via {Url}", url);
                using var httpResponse = await httpClient.PostAsJsonAsync(url, joinRequest);
                httpResponse.EnsureSuccessStatusCode();
                var response = await httpResponse.Content.ReadFromJsonAsync<ClusterJoinResponse>()
                               ?? throw new InvalidOperationException("The seed returned an empty join response.");
                return (response, seed.Address.ToString());
            }
            catch (Exception exception) {
                _logger.Warning(exception, "Failed to join via {NodeAddress}", seed.Address);
            }
        }

        throw new InvalidOperationException("Failed to join cluster: all seed nodes unreachable");
    }

    public static async Task CompleteJoinAsync(JoinToken token, ClusterJoinResponse response,
        string connectedTo, IDocumentStore documentStore, IServiceProvider services) {
        var seed = token.Seeds.Single(candidate => candidate.Address.ToString() == connectedTo);
        using var httpClient = CreateHttpClient(seed);
        await FinishJoin(token, connectedTo, httpClient);
        await WaitRavenDbReplication(documentStore, ClusterService.GetNodeTag(token.NodeNumber));

        var state = services.GetRequiredService<JoinStartupState>();
        if (state.TemporaryCertificatePath == null || state.TemporaryAwgPrivateKey == null ||
            state.TemporaryCertificatePem == null)
            throw new InvalidOperationException("Temporary join credentials are unavailable after cluster enrollment.");
        await LocalConfiguration.CertificatePfxPath.WriteAllBytesAsync(
            await File.ReadAllBytesAsync(state.TemporaryCertificatePath));
        await LocalConfiguration.CertificatePemPath.WriteAllTextAsync(state.TemporaryCertificatePem);
        await PeerCertificateTrustService.SaveAsync(state.TemporaryTrustPeers);
        new LocalConfiguration {
            NodeId = token.NodeId,
            AwgPrivateKey = state.TemporaryAwgPrivateKey,
            NodeNumber = token.NodeNumber
        }.Save();

        using var completionClient = CreateHttpClient(seed);
        using var completionResponse = await completionClient.PostAsJsonAsync(
            $"https://{connectedTo}/api/cluster/complete-join",
            new ClusterCompleteJoinRequest(token.Secret, token.NodeId, state.TemporaryCertificatePem));
        completionResponse.EnsureSuccessStatusCode();

        var temporaryDirectory = Path.GetDirectoryName(state.TemporaryCertificatePath)!;
        state.Clear();
        Directory.Delete(temporaryDirectory, true);
        _logger.Information("Node successfully joined cluster. Rebooting to complete setup");
        Environment.Exit(0);
    }

    private static async Task BootstrapSingBox(int nodeNumber, AwgGlobalSettings awgSettings,
        IEnumerable<AwgPeerInfo> peers, IEnumerable<HostAddress> seedAddresses, string awgPrivateKey,
        IOptions<SingBoxOptions> singBoxOptions, SingBoxProcessManager manager) {
        _logger.Information("Starting bootstrap sing-box for initial cluster connectivity");

        var config = new SingBoxConfig();
        var nodeIp = $"100.64.0.{nodeNumber + 10}";

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

        foreach (var peerInfo in peers) {
            var peerAddress = peerInfo.PublicAddress;
            if (string.IsNullOrEmpty(peerAddress.Host.Value)) {
                peerAddress = seedAddresses
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

        config.Log.Level = "info";

        var serializedConfig = JsonSerializer.Serialize(config, SingBoxService.SerializerOptions);
        await manager.ApplyConfigAsync(serializedConfig);
        manager.Start();
    }

    public static async Task WaitForMeshAddressAsync(IPAddress meshAddress, SingBoxProcessManager manager) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try {
            while (!NetworkInterface.GetAllNetworkInterfaces().Any(network => network.GetIPProperties()
                       .UnicastAddresses.Any(address => address.Address.Equals(meshAddress)))) {
                if (!manager.IsRunning) {
                    throw new InvalidOperationException(
                        "Bootstrap sing-box exited before the mesh interface became available");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) {
            throw new TimeoutException(
                $"Mesh interface address {meshAddress} did not become available within 120 seconds");
        }
    }

    private static async Task ConnectToAnyPeer(ClusterJoinResponse response) {
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
                        return;
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
        using var httpResponse = await httpClient.PostAsJsonAsync(url, finishJoinRequest);
        httpResponse.EnsureSuccessStatusCode();

        _logger.Information("Successfully finished joining the cluster via {Url}", url);
    }

    private static async Task WaitRavenDbReplication(IDocumentStore store, string nodeTag) {
        _logger.Information(
            "Waiting for RavenDB cluster to fully replicate to this node. If this takes too long, check the connection");
        while (true) {
            var record = await store.Maintenance.Server.SendAsync(
                new GetDatabaseRecordOperation(store.Database));

            var replicated = record != null && record.Topology.Members.Contains(nodeTag) &&
                             !record.Topology.Promotables.Contains(nodeTag);

            if (replicated) {
                _logger.Information("RavenDB cluster is fully replicated, node is ready");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}