using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Updater;
using ShadowVPN2.Updater.Docker;

namespace ShadowVPN2.Updater;

public sealed class SelfUpdater(
    DockerApiClient docker,
    IOptions<UpdaterOptions> options,
    ILogger<SelfUpdater> logger) {
    private const string ImageReferenceEnvironmentVariable = "Updater__ImageReference=";
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(250);
    private string? _oldContainerId;

    public async Task<bool> UpdateIfNeededAsync(Action releaseSocket, CancellationToken token) {
        var settings = options.Value;
        var original = await docker.InspectAsync(settings.SelfContainerName, token) ??
                       throw new InvalidOperationException("The updater container could not be found");
        if (original.Config.Labels?.TryGetValue("com.docker.compose.service", out var service) != true ||
            service != "updater")
            throw new InvalidOperationException("Target container is not the updater Compose service");

        var reference = string.IsNullOrWhiteSpace(settings.ImageReference)
            ? original.Config.Image
            : settings.ImageReference;
        if (string.IsNullOrWhiteSpace(reference))
            throw new InvalidOperationException("The updater image reference could not be determined");
        if (reference.Contains('@') || reference.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            (reference.Length == 64 && reference.All(Uri.IsHexDigit))) {
            logger.LogDebug("Skipping updater replacement for pinned image {ImageReference}", reference);
            return false;
        }

        await docker.PullAsync(reference, token);
        var image = await docker.InspectImageAsync(reference, token);
        if (string.IsNullOrWhiteSpace(image.Id))
            throw new InvalidOperationException("Docker did not return the updater image ID");
        if (string.Equals(original.Image, image.Id, StringComparison.Ordinal)) {
            logger.LogDebug("Updater already uses image {ImageId}", image.Id);
            return false;
        }

        var environment = new List<string>((original.Config.Env?.Length ?? 0) + 1);
        if (original.Config.Env is { } variables) {
            foreach (var variable in variables) {
                if (!variable.StartsWith(ImageReferenceEnvironmentVariable, StringComparison.OrdinalIgnoreCase))
                    environment.Add(variable);
            }
        }

        environment.Add(ImageReferenceEnvironmentVariable + reference);

        var request = new DockerCreateRequest {
            Image = image.Id,
            Env = environment.ToArray(),
            Labels = original.Config.Labels,
            HostConfig = new DockerCreateHostConfig {
                RestartPolicy = original.HostConfig.RestartPolicy,
                NetworkMode = original.HostConfig.NetworkMode,
                Privileged = original.HostConfig.Privileged,
                CapAdd = original.HostConfig.CapAdd,
                CapDrop = original.HostConfig.CapDrop,
                Devices = original.HostConfig.Devices,
                Binds = original.HostConfig.Binds,
                Mounts = original.HostConfig.Mounts,
                PortBindings = original.HostConfig.PortBindings,
                PublishAllPorts = original.HostConfig.PublishAllPorts,
                Dns = original.HostConfig.Dns,
                DnsSearch = original.HostConfig.DnsSearch,
                ExtraHosts = original.HostConfig.ExtraHosts,
                PidMode = original.HostConfig.PidMode,
                IpcMode = original.HostConfig.IpcMode,
                UtsMode = original.HostConfig.UtsMode,
                Init = original.HostConfig.Init,
                SecurityOpt = original.HostConfig.SecurityOpt
            }
        };

        _oldContainerId = original.Id;
        var retiringName = $"{settings.SelfContainerName}-retiring-{Guid.NewGuid():N}";
        logger.LogInformation("Replacing updater container {ContainerId} with image {ImageId}", original.Id, image.Id);
        await docker.PostActionAsync(original.Id, $"rename?name={Uri.EscapeDataString(retiringName)}", null, token);
        var successor = await docker.CreateContainerAsync(
            $"containers/create?name={Uri.EscapeDataString(settings.SelfContainerName)}", request, token);

        releaseSocket();
        await docker.PostActionAsync(successor.Id, "start", null, token);
        await WaitForSuccessorAsync(settings.SocketPath, token);
        logger.LogInformation("Successor updater {ContainerId} is listening", successor.Id);
        return true;
    }

    public async Task RetireAsync(CancellationToken token) {
        if (_oldContainerId is not { } oldId) return;
        logger.LogInformation("Removing retired updater container {ContainerId}", oldId);
        await docker.RemoveAsync(oldId, token);
    }

    private async Task WaitForSuccessorAsync(string socketPath, CancellationToken token) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ReadinessTimeout);
        var endpoint = new UnixDomainSocketEndPoint(socketPath);
        var request = JsonSerializer.Serialize(new UpdaterRequest { Operation = UpdaterOperation.Status },
            UpdaterJsonContext.Default.UpdaterRequest);
        try {
            while (true) {
                deadline.Token.ThrowIfCancellationRequested();
                using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                probeTimeout.CancelAfter(ProbeTimeout);
                try {
                    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(endpoint, probeTimeout.Token);
                    await using var stream = new NetworkStream(socket, false);
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    await writer.WriteLineAsync(request.AsMemory(), probeTimeout.Token);
                    await writer.FlushAsync(probeTimeout.Token);
                    var responseLine = await reader.ReadLineAsync(probeTimeout.Token) ??
                                       throw new IOException("The successor updater closed the readiness connection");
                    var response = JsonSerializer.Deserialize(responseLine, UpdaterJsonContext.Default.UpdaterResponse);
                    if (response is null)
                        throw new JsonException("The successor updater returned an empty readiness response");
                    return;
                }
                catch (SocketException exception) {
                    logger.LogDebug(exception, "Waiting for successor updater socket");
                }
                catch (IOException exception) {
                    logger.LogDebug(exception, "Waiting for successor updater response");
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested &&
                                                         probeTimeout.IsCancellationRequested) {
                    logger.LogDebug("Successor updater readiness probe timed out");
                }

                await Task.Delay(ProbeInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException exception) when (!token.IsCancellationRequested) {
            throw new TimeoutException("The successor updater did not become ready before the readiness deadline",
                exception);
        }
    }
}