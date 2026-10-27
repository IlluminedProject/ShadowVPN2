// Adapted from RavenDB (https://github.com/IlluminedProject/ravendb), commit 46371e89d5ae3a6202e0018d1a5671b0fc6f2c79
// Copyright RavenDB Ltd.; see LICENSE.txt in this directory

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Runtime.Loader;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations;
using Raven.Client.Exceptions;
using Raven.Client.Exceptions.Database;
using Raven.Client.Extensions;
using Raven.Client.Http;
using Raven.Client.ServerWide.Operations;
using Raven.Client.Util;
using Serilog;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace ShadowVPN2.Infrastructure.RavenDb.RavenEmbedded;

public sealed class EmbeddedServer : IDisposable {
    public static EmbeddedServer Instance = new();

    private readonly ConcurrentDictionary<string, Lazy<Task<IDocumentStore>>> _documentStores = new();

    private readonly ILogger _logger = Log.ForContext<EmbeddedServer>();

    private ServerOptions? _serverOptions;
    private Lazy<Task<(Uri ServerUrl, Process ServerProcess)>>? _serverTask;

    internal EmbeddedServer() {
    }

    public void Dispose() {
        var lazy = Interlocked.Exchange(ref _serverTask, null);
        if (lazy == null || !lazy.IsValueCreated)
            return;

        var process = lazy.Value.Result.ServerProcess;
        ShutdownServerProcess(process);
        foreach (var item in _documentStores) {
            if (item.Value.IsValueCreated) {
                item.Value.Value.Dispose();
            }
        }

        _documentStores.Clear();
    }

    public async Task<int> GetServerProcessIdAsync(CancellationToken token = default) {
        var server = _serverTask;
        if (server == null) {
            throw new InvalidOperationException(
                $"Please run {nameof(StartServer)}() before trying to use the server");
        }

        return (await server.Value.WithCancellation(token).ConfigureAwait(false)).ServerProcess.Id;
    }

    public async Task RestartServerAsync() {
        var existingServerTask = _serverTask;
        if (_serverOptions == null || existingServerTask == null || !existingServerTask.IsValueCreated) {
            throw new InvalidOperationException(
                $"Cannot call {nameof(RestartServerAsync)}() before calling {nameof(StartServer)}()");
        }

        try {
            var (_, serverProcess) = await existingServerTask.Value.ConfigureAwait(false);
            ShutdownServerProcess(serverProcess);
        }
        catch {
            // we will ignore errors here, the process might already be dead, we failed to start, etc
        }

        if (Interlocked.CompareExchange(ref _serverTask, null, existingServerTask) != existingServerTask) {
            throw new InvalidOperationException(
                $"The server changed while restarting it. Are you calling {nameof(RestartServerAsync)}() concurrently?");
        }

        await StartServerInternalAsync().ConfigureAwait(false);
    }

    public void StartServer(ServerOptions? options = null) {
        _serverOptions = options ??= ServerOptions.Default;

        if (options.Security != null) {
            try {
                var thumbprint = options.Security.ServerCertificateThumbprint;
                RequestExecutor.RemoteCertificateValidationCallback += (sender, certificate, chain, errors) =>
                    ValidateServerCertificate(sender, certificate, errors, thumbprint);
            }
            catch (NotSupportedException) {
                // not supported on Mono
            }
            catch (InvalidOperationException) {
                // not supported on MacOSX
            }
        }

        var task = StartServerInternalAsync();
        GC.KeepAlive(task); // make sure that we aren't allowing to elide the call
    }

    private static bool ValidateServerCertificate(object sender, X509Certificate? certificate,
        SslPolicyErrors errors, string? localThumbprint) {
        if (certificate == null || (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
            return false;

        // RavenDB invokes registered callbacks even when platform trust and hostname validation succeed.
        // Peer certificates are synchronized into the current user's root store as membership changes.
        if (errors == SslPolicyErrors.None)
            return true;

        // The embedded endpoint uses loopback while its certificate identifies the mesh address.
        // Limit this exception to an identifiable loopback target and the exact local certificate.
        var hostname = RequestExecutor.ConvertSenderObjectToHostname(sender);
        var isLoopback = string.Equals(hostname, "localhost", StringComparison.OrdinalIgnoreCase) ||
                         (IPAddress.TryParse(hostname, out var address) && IPAddress.IsLoopback(address));
        return isLoopback && string.Equals(certificate.GetCertHashString(), localThumbprint,
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task StopServerAsync(CancellationToken forceServerKillToken = default) {
        var existingServerTask = _serverTask;
        if (_serverOptions == null || existingServerTask == null || !existingServerTask.IsValueCreated) {
            throw new InvalidOperationException(
                $"Cannot call {nameof(StopServerAsync)}() before calling {nameof(StartServer)}()");
        }

        var (_, serverProcess) = await existingServerTask.Value.ConfigureAwait(false);

        if (forceServerKillToken.CanBeCanceled)
            forceServerKillToken.Register(() => KillServerProcess(serverProcess));

        try {
            ShutdownServerProcess(serverProcess);
        }
        catch {
            // we will ignore errors here, the process might already be dead, we failed to start, etc
        }
    }

    private Task StartServerInternalAsync() {
        var startServer = new Lazy<Task<(Uri ServerUrl, Process ServerProcess)>>(RunServer);
        if (Interlocked.CompareExchange(ref _serverTask, startServer, null) != null)
            throw new InvalidOperationException("The server was already started");

        // this forces the server to start running in an async manner.
        return startServer.Value;
    }

    public IDocumentStore GetDocumentStore(string database) {
        return AsyncHelpers.RunSync(() => GetDocumentStoreAsync(database));
    }

    public IDocumentStore GetDocumentStore(DatabaseOptions options) {
        return AsyncHelpers.RunSync(() => GetDocumentStoreAsync(options));
    }

    public Task<IDocumentStore> GetDocumentStoreAsync(string database, CancellationToken token = default) {
        return GetDocumentStoreAsync(new DatabaseOptions(database), token);
    }

    public async Task<IDocumentStore> GetDocumentStoreAsync(DatabaseOptions options,
        CancellationToken token = default) {
        var databaseName = options.DatabaseRecord.DatabaseName;
        if (string.IsNullOrWhiteSpace(databaseName)) {
            throw new ArgumentNullException(nameof(options.DatabaseRecord.DatabaseName),
                "The database name is mandatory");
        }

        if (_serverOptions == null)
            throw new InvalidOperationException("Cannot get document document before the server was started");

        if (_logger.IsEnabled(LogEventLevel.Debug))
            _logger.Debug("Creating document store for '{DatabaseName}'", databaseName);

        token.ThrowIfCancellationRequested();

        var lazy = new Lazy<Task<IDocumentStore>>(async () => {
            var serverUrl = await GetServerUriAsync(token).ConfigureAwait(false);
            serverUrl = options.LocalServerUrl ?? serverUrl;
            var store = new DocumentStore {
                Urls = new[] { serverUrl.AbsoluteUri },
                Database = databaseName,
                Certificate = _serverOptions!.Security?.ClientCertificate,
                Conventions = options.Conventions ?? new DocumentConventions()
            };

            store.Conventions.DisableTopologyCache = true;

            store.AfterDispose += (sender, args) => _documentStores.TryRemove(databaseName, out _);

            store.Initialize();
            if (!options.SkipCreatingDatabase)
                await TryCreateDatabase(options, store, token).ConfigureAwait(false);

            return store;
        });

        return await _documentStores.GetOrAdd(databaseName, lazy).Value.WithCancellation(token)
            .ConfigureAwait(false);
    }

    private async Task TryCreateDatabase(DatabaseOptions options, IDocumentStore store, CancellationToken token) {
        try {
            await store.Maintenance.SendAsync(new GetStatisticsOperation(), token).ConfigureAwait(false);
            return;
        }
        catch (DatabaseDoesNotExistException) {
            // Only a missing database requires a cluster write and an available leader.
        }

        try {
            await store.Maintenance.Server.SendAsync(new CreateDatabaseOperation(options.DatabaseRecord), token)
                .ConfigureAwait(false);
        }
        catch (ConcurrencyException) {
            // Expected behaviour when the database is already exists

            if (_logger.IsEnabled(LogEventLevel.Debug))
                _logger.Debug("{DatabaseRecordDatabaseName} already exists", options.DatabaseRecord.DatabaseName);
        }
    }

    public async Task<Uri> GetServerUriAsync(CancellationToken token = default) {
        var server = _serverTask;
        if (server == null) {
            throw new InvalidOperationException(
                $"Please run {nameof(StartServer)}() before trying to use the server");
        }

        return (await server.Value.WithCancellation(token).ConfigureAwait(false)).ServerUrl;
    }

    private void ShutdownServerProcess(Process process) {
        if (process == null || process.HasExited)
            return;

        lock (process) {
            if (process.HasExited)
                return;

            try {
                if (_logger.IsEnabled(LogEventLevel.Debug))
                    _logger.Debug("Try shutdown server PID {ProcessId} gracefully", process.Id);

                using (var inputStream = process.StandardInput) {
                    inputStream.WriteLine("shutdown no-confirmation");
                }

                if (process.WaitForExit((int)_serverOptions!.GracefulShutdownTimeout.TotalMilliseconds))
                    return;
            }
            catch (Exception e) {
                if (_logger.IsEnabled(LogEventLevel.Warning)) {
                    _logger.Warning(e,
                        "Failed to shutdown server PID {ProcessId} gracefully in {ToString}", process.Id,
                        _serverOptions!.GracefulShutdownTimeout.ToString());
                }
            }

            KillServerProcess(process);
        }
    }

    private void KillServerProcess(Process process) {
        if (process == null || process.HasExited)
            return;

        lock (process) {
            if (process.HasExited)
                return;

            try {
                if (_logger.IsEnabled(LogEventLevel.Debug))
                    _logger.Debug("Killing global server PID {ProcessId}", process.Id);

                process.Kill();
                if (!process.WaitForExit((int)_serverOptions!.ProcessKillTimeout.TotalMilliseconds)) {
                    if (_logger.IsEnabled(LogEventLevel.Information)) {
                        _logger.Information(
                            "Process {ProcessId} did not exit after Kill() within {ToString} seconds", process.Id,
                            _serverOptions!.ProcessKillTimeout.ToString());
                    }
                }
            }
            catch (Exception e) {
                if (_logger.IsEnabled(LogEventLevel.Warning)) {
                    _logger.Warning(e, "Failed to kill process {ProcessId}", process.Id);
                }
            }
        }
    }

    public event EventHandler<ServerProcessExitedEventArgs>? ServerProcessExited;

    private async Task<(Uri ServerUrl, Process ServerProcess)> RunServer() {
        if (_serverOptions == null)
            throw new ArgumentNullException(nameof(_serverOptions));

        var process = RavenServerRunner.Run(_serverOptions);
        if (_logger.IsEnabled(LogEventLevel.Debug))
            _logger.Debug("Starting global server: {ProcessId}", process.Id);

        process.Exited += (sender, e) => ServerProcessExited?.Invoke(sender, new ServerProcessExitedEventArgs());

        AssemblyLoadContext.Default.Unloading += c => {
            ShutdownServerProcess(process);
        };

        var stderrBuilder = new StringBuilder();
        var stdoutBuilder = new StringBuilder();

        var stderrTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdoutTcs =
            new TaskCompletionSource<(string Url, string Stdout)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        // DataReceived callbacks run on ThreadPool threads → can overlap.
        // Lock prevents AppendLine/ToString races.
        process.ErrorDataReceived += (_, receivedEventArgs) => {
            if (receivedEventArgs.Data == null) {
                string final;
                lock (stderrBuilder)
                    final = stderrBuilder.ToString();

                stderrTcs.TrySetResult(final);
                return;
            }

            lock (stderrBuilder)
                stderrBuilder.AppendLine(receivedEventArgs.Data);
        };

        process.OutputDataReceived += (_, receivedEventArgs) => {
            if (receivedEventArgs.Data == null)
                return;

            lock (stdoutBuilder)
                stdoutBuilder.AppendLine(receivedEventArgs.Data);

            const string prefix = "Server available on: ";
            if (!receivedEventArgs.Data.StartsWith(prefix))
                return;

            var url = receivedEventArgs.Data.Substring(prefix.Length);
            string final;
            lock (stdoutBuilder)
                final = stdoutBuilder.ToString();

            stdoutTcs.TrySetResult((url, Stdout: final));
        };

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        var timeoutTask = Task.Delay(_serverOptions.MaxServerStartupTimeDuration);

        var firstCompletedTask =
            await Task.WhenAny(stdoutTcs.Task, stderrTcs.Task, timeoutTask).ConfigureAwait(false);
        if (firstCompletedTask == stdoutTcs.Task) {
            var (url, _) = stdoutTcs.Task.Result;
            return (new Uri(url), process);
        }

        string stdoutString;
        lock (stdoutBuilder)
            stdoutString = stdoutBuilder.ToString();

        string stderrString;
        if (firstCompletedTask == stderrTcs.Task) {
            stderrString = stderrTcs.Task.Result;
            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(5)), stdoutTcs.Task)
                .ConfigureAwait(false); // extra 5 sec grace in case more error lines still arrive
        }
        else // timeout
        {
            lock (stderrBuilder)
                stderrString = stderrBuilder.ToString();
        }

        ShutdownServerProcess(process);
        throw BuildServerStartupException(stdoutString, stderrString, firstCompletedTask == timeoutTask);
    }

    private Exception BuildServerStartupException(string? outputString, string? errorString, bool isTimeout) {
        var sb = new StringBuilder();
        sb.AppendLine(isTimeout
            ? $"Server failed to start in {_serverOptions?.MaxServerStartupTimeDuration.TotalSeconds} s."
            : "Unable to start the RavenDB Server");

        if (!string.IsNullOrWhiteSpace(errorString)) {
            sb.AppendLine("Error:");
            sb.AppendLine(errorString);
        }

        if (!string.IsNullOrWhiteSpace(outputString)) {
            sb.AppendLine("Output:");
            sb.AppendLine(outputString);
        }

        return isTimeout
            ? new TimeoutException(sb.ToString())
            : new InvalidOperationException(sb.ToString());
    }

    public void OpenStudioInBrowser() {
        var serverUrl = AsyncHelpers.RunSync(() => GetServerUriAsync());
        var url = serverUrl.AbsoluteUri + "studio/index.html?disableAnalytics=true";

        if (OperatingSystem.IsWindows()) {
            Process.Start(new ProcessStartInfo("cmd", $"/c start \"Stop & look at Studio\" \"{url}\""));
            return;
        }

        if (OperatingSystem.IsMacOS()) {
            Process.Start("open", url);
            return;
        }

        Process.Start("xdg-open", url);
    }
}