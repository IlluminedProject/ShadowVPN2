using System.ComponentModel;
using System.Diagnostics;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;

namespace ShadowVPN2.Data.SingBox;

/// <summary>Owns the IPv4 NAT rules that redirect public port ranges to one backend listener.</summary>
public sealed class PortRangeRedirectService(ILogger<PortRangeRedirectService> logger) : ISingBoxExtension {
    private const string Chain = "SHADOWVPN2_PORTS";
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool hasOwnedRules;

    public Task ApplyAsync(SingBoxContext context, CancellationToken cancellationToken) {
        return ApplyAsync(context.Protocols, context.Transports, cancellationToken);
    }

    public ValueTask DisposeAsync() {
        return new ValueTask(RemoveAsync(CancellationToken.None));
    }

    private async Task ApplyAsync(IReadOnlyList<ProtocolGlobalSettings> protocols,
        IReadOnlyList<TransportSettings> transports, CancellationToken cancellationToken = default) {
        var enabledProtocols = protocols.Where(static settings => settings.Enabled)
            .ToDictionary(static settings => settings.Id);
        var ranges = new HashSet<(PortBinding Binding, ProtocolSocketKind SocketKind)>();
        foreach (var protocol in enabledProtocols.Values) {
            if (protocol.ListenPort.PortRangeEnd.HasValue)
                ranges.Add((protocol.ListenPort, ProtocolDefinitionMetadata.GetSocketKind(protocol)));
        }

        foreach (var transport in transports.OfType<FreeTurnTransportSettings>()) {
            if (transport.Enabled && transport.ListenPort.PortRangeEnd.HasValue &&
                enabledProtocols.TryGetValue(transport.ProtocolId, out var protocol)) {
                ranges.Add((transport.ListenPort, ProtocolDefinitionMetadata.GetSocketKind(protocol)));
            }
        }

        await gate.WaitAsync(cancellationToken);
        try {
            if (ranges.Count == 0) {
                if (hasOwnedRules) await RemoveRulesAsync(cancellationToken);
                return;
            }

            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("Port ranges require Linux iptables NAT REDIRECT support");

            hasOwnedRules = true;
            await EnsureChainAsync(cancellationToken);
            await RunAsync("iptables", ["-w", "5", "-t", "nat", "-F", Chain], cancellationToken);
            foreach (var (binding, socketKind) in ranges) {
                var protocol = socketKind switch {
                    ProtocolSocketKind.Udp => "udp",
                    ProtocolSocketKind.Tcp => "tcp",
                    _ => throw new ArgumentOutOfRangeException(nameof(socketKind), socketKind, null)
                };
                await RunAsync("iptables", [
                    "-w", "5", "-t", "nat", "-A", Chain, "-p", protocol, "--dport",
                    $"{binding.ListenPort}:{binding.PortRangeEnd}", "-j", "REDIRECT", "--to-ports",
                    binding.ListenPort.ToString()
                ], cancellationToken);
            }

            logger.LogInformation("Applied {Count} port range redirect rule(s) to port {Ports}",
                ranges.Count, string.Join(", ", ranges.Select(static item => item.Binding.ListenPort)));
        }
        catch (Exception ex) {
            if (ex is not OperationCanceledException)
                logger.LogError(ex, "Failed to apply port range redirects");
            try {
                if (hasOwnedRules) await RemoveRulesAsync(CancellationToken.None);
            }
            catch (Exception cleanupException) {
                logger.LogError(cleanupException, "Failed to clean up port range redirects after an apply failure");
            }

            throw;
        }
        finally {
            gate.Release();
        }
    }

    private async Task RemoveAsync(CancellationToken cancellationToken = default) {
        await gate.WaitAsync(cancellationToken);
        try {
            if (hasOwnedRules) await RemoveRulesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogError(ex, "Failed to remove port range redirects");
            throw;
        }
        finally {
            gate.Release();
        }
    }

    private async Task EnsureChainAsync(CancellationToken cancellationToken) {
        // A failed -N can mean that a previous application already created the owned chain.
        // The checked flush in ApplyAsync verifies that the chain is usable.
        await RunCheckingExistenceAsync("iptables", ["-w", "5", "-t", "nat", "-N", Chain], cancellationToken);
        var exists = await RunCheckingExistenceAsync("iptables",
            ["-w", "5", "-t", "nat", "-C", "PREROUTING", "-j", Chain], cancellationToken);
        if (!exists)
            await RunAsync("iptables", ["-w", "5", "-t", "nat", "-A", "PREROUTING", "-j", Chain], cancellationToken);
    }

    private async Task RemoveRulesAsync(CancellationToken cancellationToken) {
        if (!OperatingSystem.IsLinux()) {
            hasOwnedRules = false;
            return;
        }

        while (await RunCheckingExistenceAsync("iptables", ["-w", "5", "-t", "nat", "-C", "PREROUTING", "-j", Chain],
                   cancellationToken))
            await RunAsync("iptables", ["-w", "5", "-t", "nat", "-D", "PREROUTING", "-j", Chain], cancellationToken);
        await RunAsync("iptables", ["-w", "5", "-t", "nat", "-F", Chain], cancellationToken);
        await RunAsync("iptables", ["-w", "5", "-t", "nat", "-X", Chain], cancellationToken);
        hasOwnedRules = false;
        logger.LogInformation("Removed port range redirect rules");
    }

    private async Task RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken) {
        var result = await RunProcessAsync(fileName, arguments, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} failed ({result.ExitCode}): {result.Error}");
    }

    private static async Task<bool> RunCheckingExistenceAsync(string fileName, IEnumerable<string> arguments,
        CancellationToken cancellationToken) {
        var result = await RunProcessAsync(fileName, arguments, cancellationToken);
        if (result.ExitCode is not (0 or 1))
            throw new InvalidOperationException($"{fileName} failed ({result.ExitCode}): {result.Error}");
        return result.ExitCode == 0;
    }

    private static async Task<(int ExitCode, string Error)> RunProcessAsync(string fileName,
        IEnumerable<string> arguments, CancellationToken cancellationToken) {
        using var process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException) {
            throw new InvalidOperationException($"Unable to execute {fileName}; install iptables and grant NET_ADMIN",
                ex);
        }

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        await outputTask;
        return (process.ExitCode, await errorTask);
    }
}