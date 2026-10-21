using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ShadowVPN2.Contracts.Networking;

namespace ShadowVPN2.Updater;

public sealed class ContainerUpdater(
    DockerApiClient docker,
    ILogger<ContainerUpdater> logger,
    IOptions<UpdaterOptions> options) {
    private const string RollbackName = "shadowvpn2-rollback";
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string ContainerName {
        get => options.Value.ContainerName;
    }

    private static string Id(JsonObject container) {
        return container["Id"]!.GetValue<string>();
    }

    private static bool Running(JsonObject container) {
        return container["State"]!["Running"]!.GetValue<bool>();
    }

    private static bool Healthy(JsonObject container, string image) {
        return Running(container) &&
               container["State"]?["Health"]?["Status"]
                   ?.GetValue<string>() == "healthy" &&
               container["Config"]?["Image"]
                   ?.GetValue<string>() == image;
    }

    private Task<JsonObject> Post(string id, string action, CancellationToken token, JsonObject? body = null) {
        return docker.SendAsync($"containers/{Uri.EscapeDataString(id)}/{action}", body, token);
    }

    public async Task<ContainerImageInfo> GetImageInfoAsync(CancellationToken cancellationToken) {
        var container = await docker.InspectAsync(ContainerName, cancellationToken) ??
                        throw new InvalidOperationException("The ShadowVPN2 container could not be found");
        var image = await docker.InspectImageAsync(container["Image"]!.GetValue<string>(), cancellationToken);
        return new ContainerImageInfo {
            ImageRepository = GetImageRepository(container["Config"]!["Image"]!.GetValue<string>()),
            RepoDigests = image["RepoDigests"]?.AsArray().Select(x => x!.GetValue<string>()).ToArray() ?? [],
            Os = image["Os"]?.GetValue<string>(),
            Architecture = image["Architecture"]?.GetValue<string>(),
            Variant = image["Variant"]?.GetValue<string>()
        };
    }

    public async Task PrepareAsync(string digest, CancellationToken cancellationToken) {
        if (!ImageDigest.IsValid(digest))
            throw new ArgumentException("A canonical sha256 image digest is required", nameof(digest));
        var original = await docker.InspectAsync(ContainerName, cancellationToken) ??
                       throw new InvalidOperationException("The ShadowVPN2 container could not be found");
        var image = $"{GetImageRepository(original["Config"]!["Image"]!.GetValue<string>())}@{digest}";
        await docker.PullAsync(image, cancellationToken);
    }


    public async Task UpdateAsync(string digest, CancellationToken cancellationToken) {
        await _gate.WaitAsync(cancellationToken);
        try { await UpdateCoreAsync(digest, cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task UpdateCoreAsync(string digest, CancellationToken token) {
        if (!ImageDigest.IsValid(digest))
            throw new ArgumentException("A canonical sha256 image digest is required", nameof(digest));
        var original = await docker.InspectAsync(ContainerName, token);
        var rollback = await docker.InspectAsync(RollbackName, token);
        var source = rollback ?? original ??
            throw new InvalidOperationException("ShadowVPN2 container is missing and no rollback container exists");
        var image = $"{GetImageRepository(source["Config"]!["Image"]!.GetValue<string>())}@{digest}";
        await docker.PullAsync(image, token);
        if (rollback is not null) {
            if (original is not null && Healthy(original, image)) {
                await RemoveAsync(RollbackName, token);
                return;
            }

            if (original is not null) await RemoveAsync(Id(original), CancellationToken.None);
            await Post(RollbackName, $"rename?name={Uri.EscapeDataString(ContainerName)}", token);
            await Post(Id(rollback), "start", token);
            original = await docker.InspectAsync(ContainerName, token);
        }

        if (original is null)
            throw new InvalidOperationException("ShadowVPN2 container is missing and no rollback container exists");
        if (original["Config"]?["Labels"]?["com.docker.compose.service"]?.GetValue<string>() != "shadowvpn2")
            throw new InvalidOperationException("Target container is not the ShadowVPN2 Compose service");
        if (Healthy(original, image)) return;
        var oldId = Id(original);
        string? candidateId = null;
        var moved = false;
        try {
            await Post(oldId, "stop?t=30", token);
            await Post(oldId, $"rename?name={RollbackName}", token);
            moved = true;
            var config = original["Config"]!.DeepClone().AsObject();
            config["Image"] = image;
            var host = original["HostConfig"]!.DeepClone().AsObject();
            host["RestartPolicy"] = new JsonObject { ["Name"] = "no", ["MaximumRetryCount"] = 0 };
            config["HostConfig"] = host;
            var created = await docker.SendAsync($"containers/create?name={Uri.EscapeDataString(ContainerName)}",
                config, token);
            candidateId = created["Id"]!.GetValue<string>();
            await Post(candidateId, "start", token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            while (true) {
                var state = await docker.InspectAsync(candidateId, timeout.Token) ??
                            throw new InvalidOperationException("Updated container disappeared");
                if (!Running(state))
                    throw new InvalidOperationException("Updated ShadowVPN2 container stopped before becoming healthy");
                if (Healthy(state, image)) break;
                if (state["State"]?["Health"]?["Status"]?.GetValue<string>() == "unhealthy")
                    throw new InvalidOperationException("Updated ShadowVPN2 container reported unhealthy");
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
            }

            await Post(candidateId, "update", token,
                new JsonObject { ["RestartPolicy"] = original["HostConfig"]!["RestartPolicy"]!.DeepClone() });
        }
        catch {
            if (candidateId is not null) await RemoveAsync(candidateId, CancellationToken.None);
            else {
                var candidate = await docker.InspectAsync(ContainerName, CancellationToken.None);
                if (candidate is not null && Id(candidate) != oldId)
                    await RemoveAsync(Id(candidate), CancellationToken.None);
            }

            if (moved) await Post(oldId, $"rename?name={Uri.EscapeDataString(ContainerName)}", CancellationToken.None);
            await Post(oldId, "start", CancellationToken.None);
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
        if (Running(container)) await Post(Id(container), "stop?t=5", token);
        await docker.RemoveAsync(Id(container), token);
    }

    private static string GetImageRepository(string reference) {
        if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith("sha256:", StringComparison.Ordinal) ||
            (reference.Length == 64 && reference.All(Uri.IsHexDigit))) {
            throw new InvalidOperationException(
                "Cannot determine image repository from a container started by image ID");
        }

        var separator = reference.IndexOf('@');
        if (separator >= 0) reference = reference[..separator];
        separator = reference.LastIndexOf(':');
        return separator > reference.LastIndexOf('/') ? reference[..separator] : reference;
    }
}