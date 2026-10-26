using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Raven.Client.Documents;
using Raven.Client.Http;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Exceptions;
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
}
