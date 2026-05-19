using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Raven.Client.Documents;
using Raven.Client.ServerWide.Operations;
using Serilog;
using Serilog.Extensions.Logging;
using ShadowVPN2.Data;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Infrastructure.Configurations;
using TruePath.SystemIo;
using ILogger = Serilog.ILogger;

namespace ShadowVPN2.Infrastructure;

public class FirstLaunchExperienceHelpers
{
    private static readonly ILogger Logger = Log.ForContext<FirstLaunchExperienceHelpers>();

    public static bool IsFirstLaunch()
    {
        return !LocalConfiguration.Path.ExistsDirectory()
               || !LocalConfiguration.CertificatePfxPath.ExistsFile()
               || !LocalConfiguration.CertificatePemPath.ExistsFile()
               || !LocalConfiguration.ConfigPath.ExistsFile();
    }

    public static async Task InitializeFirstNode()
    {
        Logger.Information("Local configuration or certificates not found. Starting first-run setup");

        var localConfiguration = new LocalConfiguration
        {
            NodeNumber = 1
        };

        var (privateKey, _) = AwgKeyGenerator.GenerateKeyPair();
        localConfiguration.AwgPrivateKey = privateKey;
        localConfiguration.Save();

        Logger.Information("Generating new certificates for RavenDB cluster");
        var rootCa = RavenDbCertificates.GenerateRootCa();
        await LocalConfiguration.CertificatePemPath.WriteAllTextAsync(rootCa.ExportCertificatePem());

        var rootCaPfxBytes = rootCa.Export(X509ContentType.Pfx);
        await LocalConfiguration.RootCaPfxPath.WriteAllBytesAsync(rootCaPfxBytes);

        var (rsa, request) = RavenDbCertificates.GenerateCertificateRequest();
        var intermediateCertificate = RavenDbCertificates.SignCertificate(request, rootCa);
        var intermediateCertificateBytes = intermediateCertificate.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx);
        await LocalConfiguration.CertificatePfxPath.WriteAllBytesAsync(intermediateCertificateBytes);
        Logger.Information("Certificates generated and saved successfully");
    }

    public static async Task InitializeFromJoinToken(string joinToken, ConfigurationManager configuration)
    {
        var tokenJson = Encoding.UTF8.GetString(Convert.FromBase64String(joinToken));
        var token = JsonSerializer.Deserialize<JoinToken>(tokenJson, DataUtils.DefaultSerializerOptions)
                    ?? throw new Exception("Failed to deserialize join token");

        Logger.Information("Joining cluster as node {Name} with {NodeCount} seed addresses",
            token.Name, token.NodeAddresses.Count);

        // Trust the root CA from the token before connecting to existing nodes
        var rootCaCert = X509Certificate2.CreateFromPem(token.RootCaCertPem);
        RavenDbCertificates.TrustCustomRootCa(rootCaCert);
        Logger.Information("Trusted cluster Root CA from join token");

        // Generate AWG keypair locally
        var (awgPrivateKey, awgPublicKey) = AwgKeyGenerator.GenerateKeyPair();

        using var httpClient = CreateHttpClient();
        var clusterJoinDetails = await ExchangeToken(token, awgPublicKey, httpClient);
        await BootstrapSingBox(clusterJoinDetails, configuration, awgPrivateKey);
        var connectedTo = await ConnectToAnyPeer(clusterJoinDetails);
        var documentStore = RavenDbInitializer.Initialize(LocalConfiguration.CertificatePfxPath.ToString(),
            clusterJoinDetails.NodeNumber);
        await FinishJoin(token, connectedTo, httpClient);

        var tag = $"{token.Name}-{clusterJoinDetails.NodeNumber}";
        await WaitRavenDbReplication(documentStore, tag);
        Logger.Information("Node successfully joined cluster. Rebooting to complete setup");
        Environment.Exit(0);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
                errors is SslPolicyErrors.None
                    or SslPolicyErrors.RemoteCertificateNameMismatch
        };

        // ReSharper disable once ShortLivedHttpClient
        var httpClient = new HttpClient(handler);
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        return httpClient;
    }

    private static async Task<ClusterSignJoinResponse> ExchangeToken(JoinToken token, string awgPublicKey,
        HttpClient httpClient)
    {
        // Generate CSR for RavenDB certificate
        var (rsa, csrRequest) = RavenDbCertificates.GenerateCertificateRequest();
        var csrPem = csrRequest.CreateSigningRequestPem(
            X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1));

        var joinRequest = new ClusterSignJoinRequest
        {
            Secret = token.Secret,
            CsrPem = csrPem,
            AwgPublicKey = awgPublicKey
        };

        // Try each node address
        ClusterSignJoinResponse? response = null;
        foreach (var nodeAddress in token.NodeAddresses)
            try
            {
                var url = $"https://{nodeAddress}/api/cluster/exchange-token";
                Logger.Information("Trying to join cluster via {Url}", url);
                var httpResponse = await httpClient.PostAsJsonAsync(url, joinRequest);
                httpResponse.EnsureSuccessStatusCode();
                response = await httpResponse.Content.ReadFromJsonAsync<ClusterSignJoinResponse>();
                Logger.Information("Successfully joined cluster via {NodeAddress}", nodeAddress);
                break;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to join via {NodeAddress}", nodeAddress);
            }

        if (response == null)
            throw new Exception("Failed to join cluster: all seed nodes unreachable");

        return response;
    }

    private static async Task BootstrapSingBox(ClusterSignJoinResponse response, ConfigurationManager configuration,
        string awgPrivateKey)
    {
        Logger.Information("Starting bootstrap sing-box for initial cluster connectivity");

        // We use a temporary logger for bootstrap
        var signBoxLoggerSerilog = Log.ForContext<SingBoxProcessManager>();
        var signBoxLogger = new SerilogLoggerFactory(signBoxLoggerSerilog).CreateLogger<SingBoxProcessManager>();
        var manager = new SingBoxProcessManager(signBoxLogger, configuration);

        var config = new SingBoxConfig();
        var awgSettings = response.AwgSettings;
        var nodeIp = $"100.64.0.{response.NodeNumber + 10}";

        var endpoint = new AwgEndpointConfig
        {
            Tag = "awg-mesh-bootstrap",
            UseIntegratedTun = true,
            Address = [$"{nodeIp}/24"],
            PrivateKey = awgPrivateKey,
            ListenPort = awgSettings.ListenPort,
            Jc = awgSettings.Jc,
            Jmin = awgSettings.Jmin,
            Jmax = awgSettings.Jmax,
            S1 = awgSettings.S1,
            S2 = awgSettings.S2,
            H1 = awgSettings.H1.ToString(),
            H2 = awgSettings.H2.ToString(),
            H3 = awgSettings.H3.ToString(),
            H4 = awgSettings.H4.ToString()
        };

        foreach (var peerInfo in response.AwgPeers)
        {
            var peer = new WireGuardPeer
            {
                PublicKey = peerInfo.PublicKey,
                AllowedIps = [$"{peerInfo.MeshIp}/32"],
                Address = peerInfo.PublicHost,
                Port = awgSettings.ListenPort,
                PersistentKeepaliveInterval = 25
            };

            endpoint.Peers.Add(peer);
        }

        config.Endpoints.Add(endpoint);

        config.Outbounds.Add(new DirectOutboundConfig
        {
            Tag = "direct"
        });

        config.Log.Level = "debug";

        var serializedConfig = JsonSerializer.Serialize(config, SingBoxService.SerializerOptions);
        await manager.ApplyConfigAsync(serializedConfig);
        manager.Start();
    }

    private static async Task<string> ConnectToAnyPeer(ClusterSignJoinResponse response)
    {
        Logger.Information("Waiting for connectivity to cluster nodes...");

        // Try to connect to at least one peer's RavenDB port
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        while (!cts.IsCancellationRequested)
        {
            foreach (var peer in response.AwgPeers)
            {
                Logger.Information("Trying to connect to {PeerIp} ({PublicAddress})", peer.MeshIp, peer.PublicAddress);
                using var tcpClient = new TcpClient();
                try
                {
                    await tcpClient.ConnectAsync(peer.MeshIp, 8888, cts.Token);
                    Logger.Information("Connectivity verified to peer {PeerIp} ({PublicAddress})", peer.MeshIp,
                        peer.PublicAddress);
                    return peer.PublicAddress;
                }
                catch
                {
                    // Ignore and try next
                }
            }

            await Task.Delay(1000, cts.Token);
        }

        Logger.Fatal("Failed to verify connectivity to any cluster peer within timeout");
        throw new Exception("Failed to verify connectivity to any cluster peer within timeout");
    }

    private static async Task FinishJoin(JoinToken token, string nodeAddress, HttpClient httpClient)
    {
        var url = $"{nodeAddress}/api/cluster/finish-join";
        Logger.Information("Trying to finish joining the cluster via {Url}", url);
        var finishJoinRequest = new ClusterFinishJoinRequest(token.Secret);
        var httpResponse = await httpClient.PostAsJsonAsync(url, finishJoinRequest);
        httpResponse.EnsureSuccessStatusCode();

        Logger.Information("Successfully finished joining the cluster via {Url}", url);
    }

    private static async Task WaitRavenDbReplication(IDocumentStore store, string nodeTag)
    {
        Logger.Information(
            "Waiting for RavenDB cluster to fully replicate to this node. If this takes too long, check the connection");
        while (true)
        {
            var record = await store.Maintenance.Server.SendAsync(
                new GetDatabaseRecordOperation(store.Database));

            var replicated = record.Topology.Members.Contains(nodeTag) &&
                             !record.Topology.Promotables.Contains(nodeTag);

            if (replicated)
            {
                Logger.Information("RavenDB cluster is fully replicated, node is ready");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}