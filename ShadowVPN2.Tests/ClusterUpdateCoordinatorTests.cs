using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using Polly.Retry;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Data.Cluster;
using ShadowVPN2.Infrastructure.Configurations;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ClusterUpdateCoordinatorTests(RavenServerFixture server) : IClassFixture<RavenServerFixture> {
    [RavenServerFact]
    public async Task Coordinator_waits_when_idle_wakes_on_changes_and_polls_only_active_updates() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var store = await server.CreateStoreAsync(timeout.Token);
        using var writer = new DocumentStore { Urls = store.Urls, Database = store.Database };
        writer.Initialize();
        var attempts = new AttemptLogger();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(attempts));
        services.AddResiliencePipeline("cluster-update", pipeline => pipeline
            .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 1, Delay = TimeSpan.FromMilliseconds(20) })
            .AddTimeout(TimeSpan.FromSeconds(10)));
        await using var provider = services.BuildServiceProvider();
        var updateService = new ClusterUpdateService(store);
        using var coordinator = new ClusterUpdateCoordinator(updateService, store,
            Options.Create(new UpdaterOptions { SocketPath = "unused-updater-socket" }),
            provider.GetRequiredService<ResiliencePipelineProvider<string>>(),
            provider.GetRequiredService<ILogger<ClusterUpdateCoordinator>>());
        var requests = 0;
        store.GetRequestExecutor().OnBeforeRequest += (_, _) => Interlocked.Increment(ref requests);
        await coordinator.StartAsync(timeout.Token);
        try {
            await attempts.WaitAsync(timeout.Token);
            await AssertIdleAsync();

            var state = new ClusterUpdateState {
                Id = Guid.NewGuid(), ImageDigest = "sha256:" + new string('a', 64),
                Status = ClusterUpdateStatus.Running,
                Nodes = [
                    new ClusterUpdateNodeState { NodeTag = "another-node", Status = ClusterUpdateNodeStatus.Pending }
                ],
                StartedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            await SaveAsync(state);
            await attempts.WaitAsync(timeout.Token);
            Assert.Equal("Running", (await updateService.GetAsync(timeout.Token))!.Status);
            // There are no more document writes: this pass must be driven by the active-operation timer.
            await attempts.WaitAsync(timeout.Token);

            state.Status = ClusterUpdateStatus.Failed;
            state.Error = "Update failed on another node";
            state.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveAsync(state);
            await attempts.WaitAsync(timeout.Token);
            var history = await updateService.GetHistoryAsync(timeout.Token);
            Assert.Contains(history, entry => entry.Id == state.Id && entry.Status == "Failed");
            await AssertIdleAsync();
        }
        finally {
            await coordinator.StopAsync(CancellationToken.None);
        }

        Assert.Empty(attempts.Errors);

        async Task SaveAsync(ClusterUpdateState state) {
            using var session = writer.OpenAsyncSession(new SessionOptions
                { TransactionMode = TransactionMode.ClusterWide });
            var document =
                await session.LoadAsync<ClusterUpdateDocument>(ClusterUpdateDocument.DocumentId, timeout.Token);
            if (document is null) {
                await session.StoreAsync(new ClusterUpdateDocument { State = state }, ClusterUpdateDocument.DocumentId,
                    timeout.Token);
            }
            else
                document.State = state;

            await session.SaveChangesAsync(timeout.Token);
        }

        async Task AssertIdleAsync() {
            // Drain notifications already queued by the preceding write/connection handshake.
            await Task.Delay(300, timeout.Token);
            attempts.Drain();
            var before = Volatile.Read(ref requests);
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            idle.CancelAfter(TimeSpan.FromMilliseconds(5500));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempts.WaitAsync(idle.Token));
            Assert.Equal(before, Volatile.Read(ref requests));
            Assert.Empty(attempts.Errors);
        }
    }

    [RavenServerFact]
    public async Task Coordinator_reconciles_and_archives_when_changes_websocket_is_unavailable() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var direct = await server.CreateStoreAsync(timeout.Token);
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var proxyUrl = $"http://127.0.0.1:{((IPEndPoint)port.LocalEndpoint).Port}/";
        port.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add(proxyUrl);
        listener.Start();
        using var http = new HttpClient();
        using var proxyLifetime = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proxy = ForwardAsync();
        using var store = new DocumentStore { Urls = [proxyUrl], Database = direct.Database };
        store.Conventions.DisableTopologyUpdates = true;
        store.Initialize();
        var attempts = new AttemptLogger();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(attempts));
        services.AddResiliencePipeline("cluster-update", pipeline => pipeline
            .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 1, Delay = TimeSpan.FromMilliseconds(20) })
            .AddTimeout(TimeSpan.FromSeconds(10)));
        await using var provider = services.BuildServiceProvider();
        var service = new ClusterUpdateService(store);
        using var coordinator = new ClusterUpdateCoordinator(service, store,
            Options.Create(new UpdaterOptions { SocketPath = "unused-updater-socket" }),
            provider.GetRequiredService<ResiliencePipelineProvider<string>>(),
            provider.GetRequiredService<ILogger<ClusterUpdateCoordinator>>());
        var state = new ClusterUpdateState {
            Id = Guid.NewGuid(), ImageDigest = "sha256:" + new string('a', 64),
            Status = ClusterUpdateStatus.Running,
            Nodes = [new ClusterUpdateNodeState { NodeTag = "another-node", Status = ClusterUpdateNodeStatus.Pending }],
            StartedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveAsync();
        await coordinator.StartAsync(timeout.Token);
        try {
            await rejected.Task.WaitAsync(timeout.Token);
            Assert.Equal("Running", (await service.GetAsync(timeout.Token))!.Status);
            await attempts.WaitAsync(timeout.Token);
            await attempts.WaitAsync(timeout.Token);

            state.Status = ClusterUpdateStatus.Failed;
            state.Error = "Update failed on another node";
            await SaveAsync();
            await attempts.WaitAsync(timeout.Token);
            Assert.Contains(await service.GetHistoryAsync(timeout.Token),
                entry => entry.Id == state.Id && entry.Status == "Failed");
            Assert.Empty(attempts.Errors);
        }
        finally {
            await coordinator.StopAsync(CancellationToken.None);
            await proxyLifetime.CancelAsync();
            await proxy;
        }

        async Task SaveAsync() {
            using var session = direct.OpenAsyncSession(new SessionOptions
                { TransactionMode = TransactionMode.ClusterWide });
            var document =
                await session.LoadAsync<ClusterUpdateDocument>(ClusterUpdateDocument.DocumentId, timeout.Token);
            if (document is null) {
                await session.StoreAsync(new ClusterUpdateDocument { State = state }, ClusterUpdateDocument.DocumentId,
                    timeout.Token);
            }
            else
                document.State = state;

            await session.SaveChangesAsync(timeout.Token);
        }

        async Task ForwardAsync() {
            try {
                while (!proxyLifetime.IsCancellationRequested) {
                    var request = await listener.GetContextAsync().WaitAsync(proxyLifetime.Token);
                    if (request.Request.Url!.AbsolutePath.EndsWith("/changes", StringComparison.OrdinalIgnoreCase)) {
                        request.Response.StatusCode = 503;
                        request.Response.Close();
                        rejected.TrySetResult();
                        continue;
                    }

                    using var forwarded = new HttpRequestMessage(new HttpMethod(request.Request.HttpMethod),
                        server.ServerUrl.TrimEnd('/') + request.Request.RawUrl);
                    if (request.Request.HasEntityBody)
                        forwarded.Content = new StreamContent(request.Request.InputStream);
                    foreach (var key in request.Request.Headers.AllKeys) {
                        if (key is null or "Host" or "Connection" or "Content-Length") continue;
                        if (!forwarded.Headers.TryAddWithoutValidation(key, request.Request.Headers[key]))
                            forwarded.Content?.Headers.TryAddWithoutValidation(key, request.Request.Headers[key]);
                    }

                    using var response = await http.SendAsync(forwarded, proxyLifetime.Token);
                    request.Response.StatusCode = (int)response.StatusCode;
                    foreach (var header in response.Headers.Concat(response.Content.Headers)) {
                        if (header.Key is "Transfer-Encoding" or "Content-Length" or "Connection") continue;
                        request.Response.Headers[header.Key] = string.Join(",", header.Value);
                    }

                    var body = await response.Content.ReadAsByteArrayAsync(proxyLifetime.Token);
                    request.Response.ContentLength64 = body.Length;
                    await request.Response.OutputStream.WriteAsync(body, proxyLifetime.Token);
                    request.Response.Close();
                }
            }
            catch (OperationCanceledException) when (proxyLifetime.IsCancellationRequested) {
            }
        }
    }

    private sealed class AttemptLogger : ILoggerProvider {
        private readonly Channel<bool> _attempts = Channel.CreateUnbounded<bool>();
        public ConcurrentQueue<string> Errors { get; } = new();

        public ILogger CreateLogger(string categoryName) {
            return new Logger(this, categoryName);
        }

        public void Dispose() { }

        public Task WaitAsync(CancellationToken cancellationToken) {
            return _attempts.Reader.ReadAsync(cancellationToken).AsTask();
        }

        public void Drain() {
            while (_attempts.Reader.TryRead(out _)) { }
        }

        private sealed class Logger(AttemptLogger owner, string category) : ILogger {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) {
                if (logLevel >= LogLevel.Warning)
                    owner.Errors.Enqueue(formatter(state, exception));
                if (category == "Polly" && eventId.Id == 3)
                    owner._attempts.Writer.TryWrite(true);
            }
        }
    }
}