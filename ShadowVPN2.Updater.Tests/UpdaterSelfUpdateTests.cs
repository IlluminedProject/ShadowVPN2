using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Updater;
using ShadowVPN2.Infrastructure.Containers.Docker;
using Xunit;

namespace ShadowVPN2.Updater.Tests;

public sealed class UpdaterSelfUpdateTests {
    private const string ContainerName = "custom-updater";
    private const string ImageReference = "ghcr.io/example/shadowvpn2-updater:main";
    private static readonly string OriginalId = new('c', 64);
    private static readonly string SuccessorId = new('d', 64);
    private static readonly string OriginalImageId = "sha256:" + new string('a', 64);
    private static readonly string UpdatedImageId = "sha256:" + new string('b', 64);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unchanged_image_id_does_not_replace_or_release_updater(bool explicitImageReference) {
        var requests = new List<string>();
        using var handler = new DockerHandler((request, _) => {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            requests.Add($"{request.Method} {path}");
            if (request.Method == HttpMethod.Get && path == $"/containers/{ContainerName}/json")
                return Task.FromResult(ContainerResponse(ImageReference));
            if (request.Method == HttpMethod.Post && path == "/images/create") {
                Assert.Equal("?fromImage=" + Uri.EscapeDataString(ImageReference), request.RequestUri.Query);
                return Task.FromResult(JsonResponse(new { status = "Already exists" }));
            }

            if (request.Method == HttpMethod.Get && path == $"/images/{ImageReference}/json")
                return Task.FromResult(JsonResponse(new { Id = OriginalImageId, RepoDigests = Array.Empty<string>() }));
            throw new InvalidOperationException($"Unexpected Docker request: {request.Method} {request.RequestUri}");
        });
        using var docker = CreateDockerClient(handler);
        var updater = new SelfUpdater(docker, Options.Create(new UpdaterOptions {
            SelfContainerName = ContainerName,
            ImageReference = explicitImageReference ? ImageReference : null
        }), NullLogger<SelfUpdater>.Instance);
        var releases = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var replaced = await updater.UpdateIfNeededAsync(() => releases++, timeout.Token);
        await updater.RetireAsync(timeout.Token);

        Assert.False(replaced);
        Assert.Equal(0, releases);
        Assert.Contains("POST /images/create", requests);
        Assert.DoesNotContain(requests, request => request.StartsWith("DELETE ", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, request => request.StartsWith("POST /containers/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "ghcr.io/example/shadowvpn2-updater@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Pinned_image_reference_does_not_pull_or_replace_updater(string reference) {
        var inspections = 0;
        using var handler = new DockerHandler((request, _) => {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/containers/{ContainerName}/json", request.RequestUri!.AbsolutePath);
            inspections++;
            return Task.FromResult(ContainerResponse(reference));
        });
        using var docker = CreateDockerClient(handler);
        var updater = new SelfUpdater(docker, Options.Create(new UpdaterOptions {
            SelfContainerName = ContainerName,
            ImageReference = reference
        }), NullLogger<SelfUpdater>.Instance);
        var releases = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var replaced = await updater.UpdateIfNeededAsync(() => releases++, timeout.Token);
        await updater.RetireAsync(timeout.Token);

        Assert.False(replaced);
        Assert.Equal(0, releases);
        Assert.InRange(inspections, 0, 1);
    }

    [Fact]
    public async Task Changed_image_preserves_settings_releases_before_start_and_retires_only_original_id() {
        var events = new List<string>();
        JsonElement? createRequest = null;
        var removedIds = new List<string>();
        var socketPath = Path.Combine(Path.GetTempPath(), $"sv2-{Guid.NewGuid():N}.sock");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        Task<UpdaterRequest>? readiness = null;
        using var handler = new DockerHandler(async (request, token) => {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get && path == $"/containers/{ContainerName}/json")
                return ContainerResponse(OriginalImageId);
            if (request.Method == HttpMethod.Post && path == "/images/create") {
                Assert.Equal("?fromImage=" + Uri.EscapeDataString(ImageReference), request.RequestUri.Query);
                events.Add("pull");
                return JsonResponse(new { status = "Pull complete" });
            }

            if (request.Method == HttpMethod.Get && path == $"/images/{ImageReference}/json")
                return JsonResponse(new { Id = UpdatedImageId, RepoDigests = Array.Empty<string>() });
            if (request.Method == HttpMethod.Post && path == $"/containers/{OriginalId}/rename") {
                var renamedTo = Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?').Split('=', 2)[1]);
                Assert.StartsWith(ContainerName + "-", renamedTo);
                Assert.NotEqual(ContainerName, renamedTo);
                events.Add("rename");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post && path == "/containers/create") {
                Assert.Equal("?name=" + ContainerName, request.RequestUri.Query);
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                createRequest = json.RootElement.Clone();
                events.Add("create");
                return JsonResponse(new { Id = SuccessorId });
            }

            if (request.Method == HttpMethod.Post && path == $"/containers/{SuccessorId}/start") {
                Assert.Contains("release", events);
                events.Add("start");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Delete) {
                Assert.Equal("?force=true&v=false", request.RequestUri.Query);
                removedIds.Add(path);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected Docker request: {request.Method} {request.RequestUri}");
        });
        using var docker = CreateDockerClient(handler);
        var updater = new SelfUpdater(docker, Options.Create(new UpdaterOptions {
            SelfContainerName = ContainerName, ImageReference = ImageReference, SocketPath = socketPath
        }), NullLogger<SelfUpdater>.Instance);
        try {
            var replaced = await updater.UpdateIfNeededAsync(() => {
                events.Add("release");
                listener.Bind(new UnixDomainSocketEndPoint(socketPath));
                listener.Listen(1);
                readiness = RespondToStatusAsync(listener, timeout.Token);
            }, timeout.Token);

            Assert.True(replaced);
            Assert.NotNull(readiness);
            Assert.Equal(UpdaterOperation.Status, (await readiness.WaitAsync(timeout.Token)).Operation);
            Assert.Equal(1, events.Count(item => item == "release"));
            Assert.True(events.IndexOf("pull") < events.IndexOf("rename"));
            Assert.True(events.IndexOf("rename") < events.IndexOf("create"));
            Assert.True(events.IndexOf("create") < events.IndexOf("release"));
            Assert.True(events.IndexOf("release") < events.IndexOf("start"));
            Assert.Empty(removedIds);

            Assert.NotNull(createRequest);
            var created = createRequest.Value;
            Assert.Equal(UpdatedImageId, created.GetProperty("Image").GetString());
            var environment = created.GetProperty("Env").EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Contains("ASPNETCORE_ENVIRONMENT=Production", environment);
            Assert.Contains("CUSTOM=one=two", environment);
            Assert.Contains("Updater__SocketPath=/run/shadowvpn2/updater.sock", environment);
            Assert.Contains("Updater__SelfContainerName=" + ContainerName, environment);
            Assert.Equal("Updater__ImageReference=" + ImageReference,
                Assert.Single(environment,
                    value => value!.StartsWith("Updater__ImageReference=", StringComparison.Ordinal)));
            var host = created.GetProperty("HostConfig");
            Assert.Equal(new[] { "/var/run/docker.sock:/var/run/docker.sock", "/run/shadowvpn2:/run/shadowvpn2" },
                host.GetProperty("Binds").EnumerateArray().Select(value => value.GetString()).ToArray());
            var mount = Assert.Single(host.GetProperty("Mounts").EnumerateArray());
            Assert.Equal("volume", mount.GetProperty("Type").GetString());
            Assert.Equal("updater-cache", mount.GetProperty("Source").GetString());
            Assert.Equal("/cache", mount.GetProperty("Target").GetString());
            Assert.True(mount.GetProperty("ReadOnly").GetBoolean());
            Assert.Equal("on-failure", host.GetProperty("RestartPolicy").GetProperty("Name").GetString());
            Assert.Equal(7, host.GetProperty("RestartPolicy").GetProperty("MaximumRetryCount").GetInt32());
            Assert.Equal("host", host.GetProperty("NetworkMode").GetString());
            Assert.Equal("updater",
                created.GetProperty("Labels").GetProperty("com.docker.compose.service").GetString());
            Assert.Equal("preserved", created.GetProperty("Labels").GetProperty("custom.label").GetString());

            await updater.RetireAsync(timeout.Token);

            Assert.Equal($"/containers/{OriginalId}", Assert.Single(removedIds));
            Assert.DoesNotContain($"/containers/{SuccessorId}", removedIds);
            Assert.DoesNotContain($"/containers/{ContainerName}", removedIds);
        }
        finally {
            await timeout.CancelAsync();
            listener.Dispose();
            if (readiness is not null) {
                try {
                    await readiness;
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
                catch (SocketException) when (timeout.IsCancellationRequested) { }
            }

            File.Delete(socketPath);
        }
    }

    private static async Task<UpdaterRequest> RespondToStatusAsync(Socket listener, CancellationToken token) {
        using var connection = await listener.AcceptAsync(token);
        await using var stream = new NetworkStream(connection, false);
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true);
        var line = await reader.ReadLineAsync(token);
        var request = JsonSerializer.Deserialize(line!, UpdaterJsonContext.Default.UpdaterRequest)!;
        Assert.Equal(UpdaterOperation.Status, request.Operation);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new UpdaterResponse { Status = UpdaterStatus.Idle },
            UpdaterJsonContext.Default.UpdaterResponse).AsMemory(), token);
        await writer.FlushAsync(token);
        return request;
    }

    private static DockerApiClient CreateDockerClient(HttpMessageHandler handler) {
        var docker = new DockerApiClient(NullLogger<DockerApiClient>.Instance);
        var field = typeof(DockerApiClient).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)field.GetValue(docker)!).Dispose();
        field.SetValue(docker, new HttpClient(handler, false) {
            BaseAddress = new Uri("http://localhost/"), Timeout = Timeout.InfiniteTimeSpan
        });
        return docker;
    }

    private static HttpResponseMessage ContainerResponse(string configuredImage) {
        return JsonResponse(new {
            Id = OriginalId,
            Image = OriginalImageId,
            State = new { Running = true },
            Config = new {
                Image = configuredImage,
                Env = new[] {
                    "ASPNETCORE_ENVIRONMENT=Production", "CUSTOM=one=two",
                    "Updater__SocketPath=/run/shadowvpn2/updater.sock", "Updater__ImageReference=obsolete:previous",
                    "Updater__SelfContainerName=" + ContainerName
                },
                Labels = new Dictionary<string, string> {
                    ["com.docker.compose.service"] = "updater", ["custom.label"] = "preserved"
                }
            },
            HostConfig = new {
                RestartPolicy = new { Name = "on-failure", MaximumRetryCount = 7 },
                NetworkMode = "host",
                Binds = new[] { "/var/run/docker.sock:/var/run/docker.sock", "/run/shadowvpn2:/run/shadowvpn2" },
                Mounts = new[] { new { Type = "volume", Source = "updater-cache", Target = "/cache", ReadOnly = true } }
            }
        });
    }

    private static HttpResponseMessage JsonResponse(object value) {
        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }

    private sealed class DockerHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            return handle(request, token);
        }
    }
}