using System.Diagnostics;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Updater.Docker;

namespace ShadowVPN2.Updater;

public sealed class ContainerUpdater(
    DockerApiClient docker,
    ILogger<ContainerUpdater> logger,
    IOptions<UpdaterOptions> options) {
    private const string RollbackName = "shadowvpn2-rollback";
    private const int StopTimeoutSeconds = 30;
    private const int RollbackStopTimeoutSeconds = 5;
    private const int HealthCheckIntervalSeconds = 2;
    private const int HealthCheckTimeoutMinutes = 3;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string ContainerName => options.Value.ContainerName;

    public async Task<ContainerImageInfo> GetImageInfoAsync(CancellationToken cancellationToken) {
        logger.LogDebug("Inspecting application container {ContainerName}", ContainerName);
        var container = await docker.InspectAsync(ContainerName, cancellationToken) ??
                        throw new InvalidOperationException("The ShadowVPN2 container could not be found");
        var image = await docker.InspectImageAsync(container.Image, cancellationToken);
        var result = new ContainerImageInfo {
            ImageRepository = GetImageRepository(container.Config.Image),
            RepoDigests = image.RepoDigests,
            Os = image.Os,
            Architecture = image.Architecture,
            Variant = image.Variant
        };
        logger.LogDebug(
            "Inspected application container {ContainerName}: image {ImageId}, repository {Repository}, digests {RepoDigests}",
            ContainerName, container.Image, result.ImageRepository, string.Join(",", result.RepoDigests));
        return result;
    }

    public async Task PrepareAsync(string digest, CancellationToken cancellationToken) {
        if (!ImageDigest.IsValid(digest))
            throw new ArgumentException("A canonical sha256 image digest is required", nameof(digest));
        logger.LogInformation("Preparing image {ImageDigest} for container {ContainerName}", digest, ContainerName);
        var original = await docker.InspectAsync(ContainerName, cancellationToken) ??
                       throw new InvalidOperationException("The ShadowVPN2 container could not be found");
        var repository = GetImageRepository(original.Config.Image);
        var image = $"{repository}@{digest}";
        logger.LogInformation("Pulling image {ImageReference}", image);
        var stopwatch = Stopwatch.StartNew();
        await docker.PullAsync(image, cancellationToken);
        logger.LogInformation("Prepared image {ImageReference} in {ElapsedMilliseconds} ms", image,
            stopwatch.ElapsedMilliseconds);
    }

    public async Task<bool> IsPreparedAsync(string digest, CancellationToken cancellationToken) {
        var original = await docker.InspectAsync(ContainerName, cancellationToken) ??
                       throw new InvalidOperationException("The ShadowVPN2 container could not be found");
        var image = $"{GetImageRepository(original.Config.Image)}@{digest}";
        try {
            var inspected = await docker.InspectImageAsync(image, cancellationToken);
            return inspected.RepoDigests.Contains(image, StringComparer.Ordinal);
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    public async Task UpdateAsync(string digest, CancellationToken cancellationToken) {
        logger.LogInformation("Container update started for {ContainerName}, digest {ImageDigest}",
            ContainerName, digest);
        await _gate.WaitAsync(cancellationToken);
        try { await UpdateCoreAsync(digest, cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task UpdateCoreAsync(string digest, CancellationToken token) {
        if (!ImageDigest.IsValid(digest))
            throw new ArgumentException("A canonical sha256 image digest is required", nameof(digest));
        var rollback = await docker.InspectAsync(RollbackName, token);
        var original = await docker.InspectAsync(ContainerName, token);
        var source = original ?? rollback ??
            throw new InvalidOperationException("ShadowVPN2 container is missing and no rollback container exists");
        var image = $"{GetImageRepository(source.Config.Image)}@{digest}";
        logger.LogInformation(
            "Updating container {ContainerName} to {ImageReference}; rollback present: {RollbackPresent}",
            ContainerName, image, rollback is not null);
        if (rollback is not null) await RemoveAsync(rollback.Id, token);
        if (original is null) {
            logger.LogWarning("Primary container is missing; restoring from rollback {RollbackName}", RollbackName);
            await docker.PostActionAsync(source.Id, $"rename?name={Uri.EscapeDataString(ContainerName)}", null, token);
            await docker.PostActionAsync(source.Id, "start", null, token);
            original = await docker.InspectAsync(ContainerName, token) ??
                       throw new InvalidOperationException(
                           "ShadowVPN2 container is missing and no rollback container exists");
        }

        if (original.Config.Labels?.TryGetValue("com.docker.compose.service", out var service) != true ||
            service != "shadowvpn2")
            throw new InvalidOperationException("Target container is not the ShadowVPN2 Compose service");
        if (IsHealthy(original, image)) {
            logger.LogInformation("Container {ContainerName} is already healthy at {ImageReference}", ContainerName,
                image);
            return;
        }

        var oldId = original.Id;
        string? candidateId = null;
        var moved = false;
        try {
            logger.LogInformation("Stopping current container {ContainerId}", oldId);
            await docker.PostActionAsync(oldId, $"stop?t={StopTimeoutSeconds}", null, token);
            logger.LogInformation("Moving current container to rollback name {RollbackName}", RollbackName);
            await docker.PostActionAsync(oldId, $"rename?name={RollbackName}", null, token);
            moved = true;

            var createRequest = new DockerCreateRequest {
                Image = image,
                HostConfig = new DockerCreateHostConfig {
                    Name = original.HostConfig.RestartPolicy.Name,
                    NetworkMode = original.HostConfig.NetworkMode,
                    Privileged = original.HostConfig.Privileged,
                    CapAdd = original.HostConfig.CapAdd,
                    CapDrop = original.HostConfig.CapDrop,
                    Devices = original.HostConfig.Devices,
                    Binds = original.HostConfig.Binds,
                    PortBindings = original.HostConfig.PortBindings,
                    PublishAllPorts = original.HostConfig.PublishAllPorts,
                    Dns = original.HostConfig.Dns,
                    DnsSearch = original.HostConfig.DnsSearch,
                    ExtraHosts = original.HostConfig.ExtraHosts,
                    PidMode = original.HostConfig.PidMode,
                    IpcMode = original.HostConfig.IpcMode,
                    UtsMode = original.HostConfig.UtsMode,
                    Init = original.HostConfig.Init,
                    SecurityOpt = original.HostConfig.SecurityOpt,
                    MaximumRetryCount = 0
                },
                Labels = original.Config.Labels
            };

            logger.LogInformation("Creating candidate container from {ImageReference}", image);
            var created = await docker.CreateContainerAsync(
                $"containers/create?name={Uri.EscapeDataString(ContainerName)}", createRequest, token);
            candidateId = created.Id;
            logger.LogInformation("Starting candidate container {ContainerId}", candidateId);
            await docker.PostActionAsync(candidateId, "start", null, token);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(HealthCheckTimeoutMinutes));
            while (true) {
                var state = await docker.InspectAsync(candidateId, timeout.Token) ??
                            throw new InvalidOperationException("Updated container disappeared");
                var health = state.State.Health?.Status;
                logger.LogDebug("Candidate container {ContainerId} state: running {Running}, health {Health}",
                    candidateId, state.State.Running, health);
                if (!state.State.Running)
                    throw new InvalidOperationException("Updated ShadowVPN2 container stopped before becoming healthy");
                if (IsHealthy(state, image)) break;
                if (health == "unhealthy")
                    throw new InvalidOperationException("Updated ShadowVPN2 container reported unhealthy");
                await Task.Delay(TimeSpan.FromSeconds(HealthCheckIntervalSeconds), timeout.Token);
            }

            logger.LogInformation("Candidate container {ContainerId} is healthy; restoring restart policy",
                candidateId);
            await docker.PostActionAsync(candidateId, "update", original.HostConfig.RestartPolicy, token);
        }
        catch (Exception exception) {
            try {
                if (candidateId is not null) await RemoveAsync(candidateId, CancellationToken.None);
                else {
                    var candidate = await docker.InspectAsync(ContainerName, CancellationToken.None);
                    if (candidate is not null && candidate.Id != oldId)
                        await RemoveAsync(candidate.Id, CancellationToken.None);
                }

                if (moved)
                    await docker.PostActionAsync(oldId, $"rename?name={Uri.EscapeDataString(ContainerName)}", null,
                        CancellationToken.None);
                await docker.PostActionAsync(oldId, "start", null, CancellationToken.None);
            }
            catch (Exception rollbackException) {
                logger.LogError(exception, "Container update failed before rollback could complete");
                logger.LogError(rollbackException, "Could not restore the previous container after an update failure");
            }

            throw;
        }

        try { await RemoveAsync(RollbackName, token); }
        catch (Exception exception) {
            logger.LogWarning(exception, "Updated container is healthy but rollback container cleanup failed");
        }

        logger.LogInformation("Container updated to {ImageDigest}", digest);
    }

    private async Task RemoveAsync(string id, CancellationToken token) {
        var container = await docker.InspectAsync(id, token);
        if (container is null) return;
        if (container.State.Running)
            await docker.PostActionAsync(container.Id, $"stop?t={RollbackStopTimeoutSeconds}", null, token);
        await docker.RemoveAsync(container.Id, token);
    }

    private static bool IsHealthy(DockerContainer container, string image) =>
        container.State.Running &&
        container.State.Health?.Status == "healthy" &&
        container.Config.Image == image;

    private static string GetImageRepository(string reference) {
        if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith("sha256:", StringComparison.Ordinal) ||
            (reference.Length == 64 && reference.All(Uri.IsHexDigit))) {
            throw new InvalidOperationException(
                "Cannot determine image repository from a container started by image ID");
        }

        var atIndex = reference.IndexOf('@');
        return atIndex >= 0 ? reference[..atIndex] : reference.Split(':')[0];
    }
}