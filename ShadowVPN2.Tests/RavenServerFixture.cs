using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Raven.Client.Documents;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class RavenServerFixture : IAsyncLifetime {
    private static readonly string? ServerDirectory = FindServerDirectory();
    private readonly StringBuilder _diagnostics = new();

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ShadowVPN2.Tests", Guid.NewGuid().ToString("N"));

    private readonly ConcurrentBag<IDocumentStore> _stores = [];
    private Process? _process;
    private Task? _stderr;
    private Task? _stdout;

    public static string? UnavailableReason {
        get => ServerDirectory is null
            ? "A RavenDB server with native libraries for this platform is required. Set RAVENDB_SERVER_DIRECTORY to a RavenDB 7.2.1 server directory, or install RavenDB.Embedded 7.2.1 in the NuGet package cache."
            : null;
    }

    public string ServerUrl { get; private set; } = "";

    public async Task InitializeAsync() {
        if (ServerDirectory is null) return;
        Directory.CreateDirectory(_directory);
        var executable = Path.Combine(ServerDirectory,
            OperatingSystem.IsWindows() ? "Raven.Server.exe" : "Raven.Server");
        var startInfo = new ProcessStartInfo {
            FileName = File.Exists(executable)
                ? executable
                : Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = _directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (!File.Exists(executable))
            startInfo.ArgumentList.Add(Path.Combine(ServerDirectory, "Raven.Server.dll"));
        foreach (var argument in new[] {
                     $"--Embedded.ParentProcessId={Environment.ProcessId}",
                     "--Setup.Mode=None", "--ServerUrl=http://127.0.0.1:0", "--RunInMemory=true",
                     $"--DataDir={Path.Combine(_directory, "Data")}",
                     $"--Logs.Path={Path.Combine(_directory, "Logs")}",
                     "--License.Eula.Accepted=true", "--License.DisableAutoUpdate=true",
                     "--License.DisableAutoUpdateFromApi=true", "--License.DisableLicenseSupportCheck=true",
                     "--License.ThrowOnInvalidOrMissingLicense=false"
                 })
            startInfo.ArgumentList.Add(argument);
        foreach (var key in startInfo.Environment.Keys.ToArray()) {
            if (key.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("APP_POOL_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("IIS_", StringComparison.OrdinalIgnoreCase))
                startInfo.Environment.Remove(key);
        }

        startInfo.Environment["DOTNET_GCDynamicAdaptationMode"] = "1";
        startInfo.Environment["DOTNET_GCRetainVM"] = "0";
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try {
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("RavenDB server did not start");
            _stdout = CaptureOutputAsync(_process.StandardOutput, ready);
            _stderr = CaptureOutputAsync(_process.StandardError);
            ServerUrl = await ready.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (Exception exception) {
            await DisposeAsync();
            throw new InvalidOperationException(
                $"Could not start the integration-test RavenDB server in {ServerDirectory}. {_diagnostics}", exception);
        }
    }

    public async Task DisposeAsync() {
        while (_stores.TryTake(out var store))
            store.Dispose();
        if (_process is not null) {
            if (!_process.HasExited) {
                try {
                    await _process.StandardInput.WriteLineAsync("shutdown no-confirmation");
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception exception) when (exception is TimeoutException or IOException
                                                      or InvalidOperationException) {
                    if (!_process.HasExited)
                        _process.Kill(true);
                    await _process.WaitForExitAsync();
                }
            }

            if (_stdout is not null) await _stdout;
            if (_stderr is not null) await _stderr;
            _process.Dispose();
            _process = null;
        }

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    public async Task<IDocumentStore> CreateStoreAsync(CancellationToken cancellationToken = default) {
        if (ServerDirectory is null)
            throw new InvalidOperationException(UnavailableReason);
        var store = new DocumentStore {
            Urls = [ServerUrl], Database = "ClusterUpdates-" + Guid.NewGuid().ToString("N")
        };
        store.Conventions.DisableTopologyCache = true;
        store.Initialize();
        try {
            await store.Maintenance.Server.SendAsync(new CreateDatabaseOperation(new DatabaseRecord(store.Database) {
                Settings = new Dictionary<string, string> { ["RunInMemory"] = "true" }
            }), cancellationToken);
            _stores.Add(store);
            return store;
        }
        catch {
            store.Dispose();
            throw;
        }
    }

    private async Task CaptureOutputAsync(StreamReader reader, TaskCompletionSource<string>? ready = null) {
        while (await reader.ReadLineAsync() is { } line) {
            lock (_diagnostics) {
                if (_diagnostics.Length < 16384)
                    _diagnostics.AppendLine(line);
            }

            const string prefix = "Server available on: ";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                ready?.TrySetResult(line[prefix.Length..].Trim());
        }

        ready?.TrySetException(new InvalidOperationException("RavenDB exited before announcing its server URL"));
    }

    private static string? FindServerDirectory() {
        var nativeLibrary =
            (OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), RuntimeInformation.ProcessArchitecture) switch {
                (true, _, Architecture.X64) => "librvnpal.win.x64.dll",
                (true, _, Architecture.X86) => "librvnpal.win.x86.dll",
                (_, true, Architecture.X64) => "librvnpal.linux.x64.so",
                (_, true, Architecture.Arm64) => "librvnpal.arm.64.so",
                (false, false, Architecture.X64) when OperatingSystem.IsMacOS() => "librvnpal.mac.x64.dylib",
                (false, false, Architecture.Arm64) when OperatingSystem.IsMacOS() => "librvnpal.mac.arm64.dylib",
                _ => null
            };
        if (nativeLibrary is null) return null;
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget",
                           "packages");
        foreach (var candidate in new[] {
                     Environment.GetEnvironmentVariable("RAVENDB_SERVER_DIRECTORY"),
                     Path.Combine(AppContext.BaseDirectory, "RavenDBServer"),
                     Path.Combine(packages, "ravendb.embedded", "7.2.1", "contentFiles", "any", "any", "RavenDBServer")
                 }) {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                File.Exists(Path.Combine(candidate, "Raven.Server.dll")) &&
                File.Exists(Path.Combine(candidate, nativeLibrary)))
                return Path.GetFullPath(candidate);
        }

        return null;
    }
}