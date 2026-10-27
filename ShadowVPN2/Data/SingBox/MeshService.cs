using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Data.SingBox.Models;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.SingBox;

public sealed class MeshService(
    ILogger<MeshService> logger,
    IOptions<LocalConfiguration> localConfiguration,
    IOptions<SingBoxOptions> options,
    SingBoxProcessManager processManager,
    IServiceProvider services) : BackgroundService {
    private readonly SemaphoreSlim _configurationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SingBoxProcessHandle _process = processManager.Create("mesh");
    private int _disposed;

    public async Task BootstrapAsync(int nodeNumber, string privateKey, AwgGlobalSettings settings,
        IEnumerable<WireGuardPeer> peers, CancellationToken ct) {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var config = Build(nodeNumber, privateKey, options.Value.Awg.UseIntegratedTun, settings, peers);
        await ApplyConfigAsync(JsonSerializer.Serialize(config, SingBoxService.SerializerOptions), cancellation.Token);
        await WaitForLocalAddressAsync(nodeNumber, cancellation.Token);
    }

    public async Task InitializeFromDatabaseAsync(CancellationToken ct) {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var documentStore = services.GetRequiredService<IDocumentStore>();
        var networkService = services.GetRequiredService<NodeNetworkService>();
        var nodeNumber = await RegenerateAsync(documentStore, networkService, cancellation.Token);
        await WaitForLocalAddressAsync(nodeNumber, cancellation.Token);
    }

    private async Task WaitForLocalAddressAsync(int nodeNumber, CancellationToken ct) {
        var expectedAddress = IPAddress.Parse($"100.64.0.{nodeNumber + 10}");
        var timeout = Stopwatch.StartNew();
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (!_process.IsRunning)
                throw new InvalidOperationException("Mesh sing-box stopped before its local address became available.");

            if (NetworkInterface.GetAllNetworkInterfaces().Any(network => network.GetIPProperties()
                    .UnicastAddresses.Any(address => address.Address.Equals(expectedAddress))))
                return;

            if (timeout.Elapsed >= TimeSpan.FromSeconds(15)) {
                throw new TimeoutException(
                    $"Mesh address {expectedAddress} did not become available before cluster startup.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifetime.Token);
        var ct = cancellation.Token;
        try {
            while (!ct.IsCancellationRequested) {
                try {
                    var documentStore = services.GetRequiredService<IDocumentStore>();
                    var networkService = services.GetRequiredService<NodeNetworkService>();
                    await ObserveConfigurationAsync(documentStore, networkService, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    break;
                }
                catch (Exception exception) {
                    logger.LogError(exception, "Mesh database observation failed; retrying");
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task ObserveConfigurationAsync(IDocumentStore documentStore,
        NodeNetworkService networkService, CancellationToken ct) {
        var changes = documentStore.Changes();
        var nodes = changes.ForDocumentsInCollection<EntityClusterNode>();
        var networks = changes.ForDocumentsInCollection<EntityNodeNetworkStatus>();
        var global = changes.ForDocument(EntityGlobalConfiguration.ConfigurationDocumentId);
        var reconnected = Observable.FromEventPattern(
                handler => changes.ConnectionStatusChanged += handler,
                handler => changes.ConnectionStatusChanged -= handler)
            .Select(_ => Unit.Default);
        long revision = 0;
        var updates = Observable.Merge(nodes, networks, global).Select(_ => Unit.Default)
            .Merge(reconnected)
            .Select(_ => Interlocked.Increment(ref revision))
            .Replay(1);
        using var connection = updates.Connect();
        using (var setup = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            setup.CancelAfter(TimeSpan.FromSeconds(15));
            await changes.EnsureConnectedNow().WaitAsync(setup.Token);
            await Task.WhenAll(nodes.EnsureSubscribedNow(), networks.EnsureSubscribedNow(),
                global.EnsureSubscribedNow()).WaitAsync(setup.Token);
        }

        while (true) {
            var processed = Volatile.Read(ref revision);
            try {
                await RegenerateAsync(documentStore, networkService, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            }
            catch (Exception exception) {
                logger.LogError(exception, "Failed to regenerate mesh configuration; retrying");
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                continue;
            }

            await updates.FirstAsync(request => request > processed).ToTask(ct);
        }
    }

    private async Task<int> RegenerateAsync(IDocumentStore documentStore,
        NodeNetworkService networkService, CancellationToken ct) {
        var local = localConfiguration.Value;
        if (string.IsNullOrEmpty(local.AwgPrivateKey))
            throw new InvalidOperationException("Local mesh private key is unavailable.");
        using var session = documentStore.OpenAsyncSession();
        var global = await session.LoadAsync<EntityGlobalConfiguration>(
            EntityGlobalConfiguration.ConfigurationDocumentId, ct);
        var nodes = await session.Advanced
            .LoadStartingWithAsync<EntityClusterNode>("EntityClusterNodes/", pageSize: int.MaxValue, token: ct);
        var localNode = nodes.FirstOrDefault(node => node.NodeId == local.NodeId);
        var nodeNumber = localNode?.Number ?? local.NodeNumber;
        var peers = new List<WireGuardPeer>();
        if (global != null) {
            foreach (var node in nodes.Where(node => node.NodeId != local.NodeId && node.AwgPublicKey != null)
                         .OrderBy(node => node.Number)) {
                var peer = new WireGuardPeer {
                    PublicKey = node.AwgPublicKey!,
                    AllowedIps = [$"{node.AwgMeshIp}/32"],
                    PersistentKeepaliveInterval = 25
                };
                var host = await networkService.GetPublicHostAsync(node, ct);
                if (host.HasValue) {
                    peer.Address = host.Value.FormatForUri();
                    peer.Port = global.AwgSettings.ListenPort.ListenPort;
                }

                peers.Add(peer);
            }
        }

        var config = Build(nodeNumber, local.AwgPrivateKey, options.Value.Awg.UseIntegratedTun, global?.AwgSettings,
            peers);
        await ApplyConfigAsync(JsonSerializer.Serialize(config, SingBoxService.SerializerOptions), ct);
        return nodeNumber;
    }

    private async Task ApplyConfigAsync(string configJson, CancellationToken ct) {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await _configurationGate.WaitAsync(cancellation.Token);
        try {
            await _process.ApplyConfigAsync(configJson, cancellation.Token);
        }
        finally {
            _configurationGate.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        _lifetime.Cancel();
        try {
            await base.StopAsync(cancellationToken);
        }
        finally {
            await _configurationGate.WaitAsync(CancellationToken.None);
            try {
                await _process.DisposeAsync();
            }
            finally {
                _configurationGate.Release();
            }
        }
    }

    public override void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime.Cancel();
        try {
            base.Dispose();
        }
        finally {
            _configurationGate.Wait();
            try {
                _process.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally {
                _configurationGate.Release();
            }
        }
    }

    public SingBoxConfig Build(int nodeNumber, string privateKey, bool useIntegratedTun,
        AwgGlobalSettings? settings, IEnumerable<WireGuardPeer> peers) {
        var endpoint = new AwgEndpointConfig {
            Tag = "awg-mesh",
            UseIntegratedTun = useIntegratedTun,
            Address = [$"100.64.0.{nodeNumber + 10}/24"],
            PrivateKey = privateKey,
            ListenPort = settings?.ListenPort.ListenPort ?? 0,
            Jc = settings?.Jc,
            Jmin = settings?.Jmin,
            Jmax = settings?.Jmax,
            S1 = settings?.S1,
            S2 = settings?.S2,
            S3 = settings?.S3,
            S4 = settings?.S4,
            H1 = settings?.H1,
            H2 = settings?.H2,
            H3 = settings?.H3,
            H4 = settings?.H4,
            I1 = settings?.I1,
            I2 = settings?.I2,
            I3 = settings?.I3,
            I4 = settings?.I4,
            I5 = settings?.I5,
            Peers = peers.ToList()
        };
        var config = new SingBoxConfig();
        config.Endpoints.Add(endpoint);
        config.Outbounds.Add(new DirectOutboundConfig { Tag = "direct" });
        config.Log.Level = "info";
        return config;
    }
}