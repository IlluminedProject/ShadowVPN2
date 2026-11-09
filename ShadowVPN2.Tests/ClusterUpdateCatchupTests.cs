using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.Registry;
using Raven.Client.Documents.Session;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Updater;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Infrastructure.Configurations;
using ShadowVPN2.Infrastructure.Containers.Registry;
using Xunit;
using NodeStatus = ShadowVPN2.Data.Cluster.ClusterUpdateNodeStatus;

namespace ShadowVPN2.Tests;

public sealed class ClusterUpdateCatchupTests(RavenServerFixture server) : IClassFixture<RavenServerFixture> {
    [RavenServerTheory]
    [InlineData(true, null, null, false)]
    [InlineData(false, "2026-11-09T12:00:00Z", "2026-11-08T12:00:00Z", false)]
    [InlineData(false, "2026-11-09T12:00:00Z", "2026-11-09T12:00:00Z", false)]
    [InlineData(false, null, "2026-11-09T12:00:00Z", false)]
    [InlineData(false, "2026-11-09T12:00:00Z", null, false)]
    [InlineData(false, "2026-11-08T12:00:00Z", "2026-11-09T12:00:00Z", true)]
    [InlineData(false, "2026-11-09T14:00:00+02:00", "2026-11-09T12:00:00Z", false)]
    public async Task Catchup_updates_only_newer_images_and_completes_matching_nodes(
        bool matches, string? currentCreated, string? targetCreated, bool shouldStart) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var topology = await store.Maintenance.Server.SendAsync(new GetClusterTopologyOperation(), timeout.Token);
        var config = JsonSerializer.SerializeToUtf8Bytes(new { created = targetCreated });
        var configDigest = Hash(config);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new { config = new { digest = configDigest } });
        var digest = Hash(manifest);
        var state = new ClusterUpdateState {
            Id = Guid.NewGuid(), ImageDigest = digest, DesiredImageDigest = digest,
            Status = ClusterUpdateStatus.Completed, CurrentNodeIndex = 1,
            StartedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Nodes = [
                new ClusterUpdateNodeState {
                    NodeTag = topology.NodeTag, Status = NodeStatus.Skipped, Error = "Preparation timed out"
                }
            ]
        };
        using (var session =
               store.OpenAsyncSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide })) {
            await session.StoreAsync(new ClusterUpdateDocument { State = state }, ClusterUpdateDocument.DocumentId,
                timeout.Token);
            await session.SaveChangesAsync(timeout.Token);
        }

        var requests = new List<UpdaterOperation>();
        var running = false;
        var socketPath = Path.Combine(Path.GetTempPath(), $"sv2-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(10);
        var responses = RespondAsync();
        using var handler = new RegistryHandler(request => {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            return path switch {
                var value when value == "/v2/example/app/manifests/" + digest => manifest,
                var value when value == "/v2/example/app/blobs/" + configDigest => config,
                _ => throw new InvalidOperationException("Unexpected registry request: " + path)
            };
        });
        var registry = new RegistryImageClient(new ClientFactory(handler));
        try {
            var updater = new UpdaterClient(Options.Create(new UpdaterOptions { SocketPath = socketPath }),
                NullLogger<UpdaterClient>.Instance);
            using var coordinator = new ClusterUpdateCoordinator(new ClusterUpdateService(store), store, updater,
                registry, new ResiliencePipelineRegistry<string>(), NullLogger<ClusterUpdateCoordinator>.Instance);
            var reconcile =
                typeof(ClusterUpdateCoordinator).GetMethod("ReconcileAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

            async Task<bool> ReconcileAsync() {
                return await (Task<bool>)reconcile.Invoke(coordinator, [timeout.Token])!;
            }

            Assert.Equal(shouldStart, await ReconcileAsync());
            if (shouldStart) {
                Assert.Contains(UpdaterOperation.Start, requests);
                // Poll while the old application still runs: do not prepare/start another operation.
                Assert.True(await ReconcileAsync());
                Assert.Equal(1, requests.Count(operation => operation == UpdaterOperation.Start));
                matches = true;
                Assert.False(await ReconcileAsync());
            }
            else {
                Assert.DoesNotContain(UpdaterOperation.Prepare, requests);
                Assert.DoesNotContain(UpdaterOperation.Start, requests);
            }

            using var session = store.OpenAsyncSession();
            var saved =
                (await session.LoadAsync<ClusterUpdateDocument>(ClusterUpdateDocument.DocumentId, timeout.Token))!
                .State!;
            Assert.Equal(matches ? NodeStatus.Completed : NodeStatus.Skipped, saved.Nodes[0].Status);
            if (matches) {
                Assert.Null(saved.Nodes[0].Error);
                Assert.NotNull(saved.Nodes[0].CompletedAt);
                // A later manual image change must not reactivate the completed catch-up.
                matches = false;
                requests.Clear();
                Assert.False(await ReconcileAsync());
                Assert.Empty(requests);
            }

            Assert.Equal(ClusterUpdateStatus.Completed, saved.Status);
            Assert.Equal(1, saved.CurrentNodeIndex);
            Assert.Equal(state.UpdatedAt, saved.UpdatedAt);
        }
        finally {
            await timeout.CancelAsync();
            listener.Dispose();
            try { await responses; }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            catch (SocketException) when (timeout.IsCancellationRequested) { }

            File.Delete(socketPath);
        }

        async Task RespondAsync() {
            while (!timeout.IsCancellationRequested) {
                using var socket = await listener.AcceptAsync(timeout.Token);
                await using var stream = new NetworkStream(socket, false);
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                var request = JsonSerializer.Deserialize((await reader.ReadLineAsync(timeout.Token))!,
                    UpdaterJsonContext.Default.UpdaterRequest)!;
                requests.Add(request.Operation);
                var response = new UpdaterResponse { UpdateId = request.UpdateId, ImageDigest = digest };
                switch (request.Operation) {
                    case UpdaterOperation.LocalStatus:
                        response = response with {
                            ImageDigest = matches ? digest : "", Status = UpdaterStatus.Succeeded,
                            ImageInfo = new ContainerImageInfo {
                                ImageRepository = "registry.example/example/app", Os = "linux", Architecture = "amd64",
                                CreatedAt = currentCreated is null ? null : DateTimeOffset.Parse(currentCreated)
                            }
                        };
                        break;
                    case UpdaterOperation.Status:
                        response = response with { Status = running ? UpdaterStatus.Running : UpdaterStatus.Idle };
                        break;
                    case UpdaterOperation.Prepare:
                        response = response with { Status = UpdaterStatus.Prepared };
                        break;
                    case UpdaterOperation.Start:
                        running = true;
                        response = response with { Status = UpdaterStatus.Running };
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected updater request: " + request.Operation);
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response,
                    UpdaterJsonContext.Default.UpdaterResponse));
                await writer.FlushAsync(timeout.Token);
            }
        }
    }

    [RavenServerTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stale_catchup_cannot_complete_a_replaced_rollout(bool differentOperation) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var store = await server.CreateStoreAsync(timeout.Token);
        var digest = "sha256:" + new string('a', 64);
        var id = Guid.NewGuid();
        using (var session =
               store.OpenAsyncSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide })) {
            await session.StoreAsync(new ClusterUpdateDocument {
                State = new ClusterUpdateState {
                    Id = id, ImageDigest = digest, Status = ClusterUpdateStatus.Completed,
                    Nodes = [new ClusterUpdateNodeState { NodeTag = "A", Status = NodeStatus.Skipped }]
                }
            }, ClusterUpdateDocument.DocumentId, timeout.Token);
            await session.SaveChangesAsync(timeout.Token);
        }

        await new ClusterUpdateService(store).CompleteCatchupAsync("A", differentOperation ? Guid.NewGuid() : id,
            differentOperation ? digest : "sha256:" + new string('b', 64), timeout.Token);
        using var read = store.OpenAsyncSession();
        var saved = await read.LoadAsync<ClusterUpdateDocument>(ClusterUpdateDocument.DocumentId, timeout.Token);
        Assert.Equal(NodeStatus.Skipped, saved!.State!.Nodes[0].Status);
    }

    private static string Hash(byte[] bytes) {
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private sealed class RegistryHandler(Func<HttpRequestMessage, byte[]> respond) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(respond(request)) });
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory {
        public HttpClient CreateClient(string name) {
            return new HttpClient(handler, false);
        }
    }
}