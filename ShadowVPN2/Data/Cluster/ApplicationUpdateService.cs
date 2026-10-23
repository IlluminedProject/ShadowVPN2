using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Exceptions;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.Cluster;

public sealed class ApplicationUpdateService(
    RegistryImageClient registry,
    UpdaterImageClient updater,
    ClusterUpdateService clusterUpdates,
    IDocumentStore documentStore,
    IOptions<UpdaterOptions> options,
    ILogger<ApplicationUpdateService> logger) {
    private readonly object _cacheGate = new();
    private Task<UpdateAvailabilityResponse>? _check;
    private DateTimeOffset _expires;
    private (Guid Id, string Status)? _lastTerminal;

    public async Task<UpdateAvailabilityResponse> CheckAsync(CancellationToken cancellationToken = default,
        bool forceRefresh = false) {
        Task<UpdateAvailabilityResponse> check;
        lock (_cacheGate) {
            if (_check is null || (_check.IsCompleted && (forceRefresh || DateTimeOffset.UtcNow >= _expires))) {
                // A caller disconnecting must not cancel the shared check or cause a second registry request.
                _check = DiscoverAsync();
                var interval = options.Value.CheckInterval;
                _expires = DateTimeOffset.UtcNow + (interval > TimeSpan.Zero ? interval : TimeSpan.FromMinutes(30));
            }

            check = _check;
        }

        var result = await check.WaitAsync(cancellationToken);
        return new UpdateAvailabilityResponse {
            ImageReference = result.ImageReference, CurrentDigest = result.CurrentDigest,
            AvailableDigest = result.AvailableDigest, UpdateAvailable = result.UpdateAvailable,
            CheckedAt = result.CheckedAt, Error = result.Error, Enabled = result.Enabled
        };
    }

    public async Task<ClusterUpdateResponse?> GetStatusAsync(CancellationToken cancellationToken = default) {
        var status = await clusterUpdates.GetAsync(cancellationToken);
        if (status is { Status: not "Running" }) {
            lock (_cacheGate) {
                if (_lastTerminal != (status.Id, status.Status)) {
                    _lastTerminal = (status.Id, status.Status);
                    _expires = DateTimeOffset.MinValue;
                }
            }
        }

        return status;
    }

    public Task<IReadOnlyList<ApplicationUpdateHistoryResponse>> GetHistoryAsync(
        CancellationToken cancellationToken = default) {
        return clusterUpdates.GetHistoryAsync(cancellationToken);
    }

    public async Task<ClusterUpdateResponse> StartAsync(bool backwardCompatible,
        CancellationToken cancellationToken = default) {
        var current = await GetStatusAsync(cancellationToken);
        if (current?.Status == "Running")
            throw new AppException(StatusCodes.Status409Conflict, "A cluster update is already active");
        var available = await CheckAsync(cancellationToken);
        if (!available.Enabled || available.Error is not null || !available.UpdateAvailable ||
            available.AvailableDigest is null)
            throw new AppException(StatusCodes.Status409Conflict,
                available.Error ?? "No application update is available");
        var topology =
            await documentStore.Maintenance.Server.SendAsync(new GetClusterTopologyOperation(), cancellationToken);
        return await clusterUpdates.StartAsync(new StartClusterUpdateRequest {
            ImageDigest = available.AvailableDigest, BackwardCompatible = backwardCompatible
        }, topology, topology.NodeTag, cancellationToken);
    }

    private async Task<UpdateAvailabilityResponse> DiscoverAsync() {
        var reference = options.Value.ImageReference?.Trim();
        var result = new UpdateAvailabilityResponse {
            ImageReference = reference, CheckedAt = DateTimeOffset.UtcNow
        };
        if (!Path.Exists(options.Value.SocketPath))
            return result;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try {
            var image = await updater.GetAsync(timeout.Token);
            result.ImageReference ??= image.ImageRepository;
            var actual = RegistryImageClient.ParseReference(image.ImageRepository ?? "");
            var digests = image.RepoDigests.Where(x => {
                var separator = x.LastIndexOf('@');
                if (separator < 0 || !ImageDigest.IsValid(x[(separator + 1)..])) return false;
                var repository = RegistryImageClient.ParseReference(x[..separator]);
                return repository.Registry == actual.Registry && repository.Repository == actual.Repository;
            }).Select(x => x[(x.LastIndexOf('@') + 1)..]).ToArray();
            if (digests.Length == 0) {
                throw new InvalidOperationException(
                    "Docker has no repository digest for the running application image");
            }

            result.CurrentDigest = digests[0];
            if (string.IsNullOrWhiteSpace(reference)) {
                result.Error = "Update checks require an application image reference in Updater:ImageReference";
                return result;
            }

            if (reference.Contains('@')) {
                result.Error = "Update checks are disabled because the application image is pinned to a digest";
                return result;
            }

            var configured = RegistryImageClient.ParseReference(reference);
            result.Enabled = true;
            if (configured.Registry != actual.Registry || configured.Repository != actual.Repository) {
                throw new InvalidOperationException(
                    "The configured update image repository does not match the running container");
            }

            var remote = await registry.GetDigestAsync(reference, image, timeout.Token);
            result.AvailableDigest = remote.Digest;
            // Docker can retain either the multi-platform index digest or its selected platform manifest digest.
            result.CurrentDigest = digests.FirstOrDefault(x => x == remote.Digest) ??
                                   digests.FirstOrDefault(x => x == remote.PlatformDigest) ?? digests[0];
            result.UpdateAvailable = !digests.Contains(remote.Digest, StringComparer.Ordinal) &&
                                     (remote.PlatformDigest is null ||
                                      !digests.Contains(remote.PlatformDigest, StringComparer.Ordinal));
        }
        catch (Exception exception) when (exception is SocketException or IOException or HttpRequestException or
                                              OperationCanceledException or InvalidOperationException or JsonException
                                              or ArgumentException) {
            logger.LogWarning(exception, "Application update discovery failed for {ImageReference}", reference);
            result.Error = exception switch {
                SocketException or IOException =>
                    "The updater is unavailable or could not read the running container image. Check that the updater service is running.",
                OperationCanceledException =>
                    "The update check timed out. The updater or image registry may be unavailable.",
                HttpRequestException => "The image registry is unavailable. Try again after the next update check.",
                InvalidOperationException => exception.Message,
                _ => "The updater or image registry returned information that could not be read."
            };
        }

        result.CheckedAt = DateTimeOffset.UtcNow;
        return result;
    }
}