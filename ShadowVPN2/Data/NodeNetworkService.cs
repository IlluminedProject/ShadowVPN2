using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data;

public sealed class NodeNetworkService(
    IDocumentStore documentStore,
    IHttpClientFactory httpClientFactory,
    IOptions<LocalConfiguration> localConfiguration,
    ILogger<NodeNetworkService> logger) : BackgroundService {
    public static string GetStatusId(Guid nodeId) => $"NodeNetworkStatuses/{nodeId:D}";

    public async Task<EntityNodeNetworkStatus?> GetStatusAsync(Guid nodeId,
        CancellationToken cancellationToken = default) {
        using var session = documentStore.OpenAsyncSession();
        return await session.LoadAsync<EntityNodeNetworkStatus>(GetStatusId(nodeId), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, EntityNodeNetworkStatus>> GetStatusesAsync(
        IEnumerable<Guid> nodeIds,
        CancellationToken cancellationToken = default) {
        var ids = nodeIds.Distinct().ToDictionary(GetStatusId, nodeId => nodeId);
        if (ids.Count == 0)
            return new Dictionary<Guid, EntityNodeNetworkStatus>();

        using var session = documentStore.OpenAsyncSession();
        var statuses = await session.LoadAsync<EntityNodeNetworkStatus>(ids.Keys, cancellationToken);
        return statuses
            .Where(pair => pair.Value != null)
            .ToDictionary(pair => ids[pair.Key], pair => pair.Value!);
    }

    public async Task<string?> GetPublicHostAsync(EntityClusterNode node,
        CancellationToken cancellationToken = default) {
        if (!string.IsNullOrWhiteSpace(node.Domain))
            return node.Domain;

        var status = await GetStatusAsync(node.NodeId, cancellationToken);
        return status?.PublicIpv4 ?? status?.PublicIpv6;
    }

    public async Task RecordObservedPublicIpAsync(Guid nodeId, string? value,
        CancellationToken cancellationToken = default) {
        if (!IPAddress.TryParse(value, out var address))
            return;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        using var session = documentStore.OpenAsyncSession();
        var id = GetStatusId(nodeId);
        var status = await session.LoadAsync<EntityNodeNetworkStatus>(id, cancellationToken)
                     ?? new EntityNodeNetworkStatus { Id = id, NodeId = nodeId };

        if (address.AddressFamily == AddressFamily.InterNetwork)
            status.PublicIpv4 = address.ToString();
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
            status.PublicIpv6 = address.ToString();

        status.CheckedAt = DateTimeOffset.UtcNow;
        status.LastError = null;
        await session.StoreAsync(status, id, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task<EntityNodeNetworkStatus> RefreshLocalAsync(CancellationToken cancellationToken = default) {
        var nodeId = localConfiguration.Value.NodeId;
        var id = GetStatusId(nodeId);

        using var session = documentStore.OpenAsyncSession();
        var status = await session.LoadAsync<EntityNodeNetworkStatus>(id, cancellationToken)
                     ?? new EntityNodeNetworkStatus { Id = id, NodeId = nodeId };

        try {
            status.InternalAddresses = GetInternalAddresses();
            status.PublicIpv4 = await GetPublicIpAsync("https://api4.ipify.org", AddressFamily.InterNetwork,
                cancellationToken) ?? status.PublicIpv4;
            status.PublicIpv6 = await GetPublicIpAsync("https://api6.ipify.org", AddressFamily.InterNetworkV6,
                cancellationToken) ?? status.PublicIpv6;
            status.LastError = null;
        }
        catch (Exception ex) {
            status.LastError = ex.Message;
            logger.LogWarning(ex, "Failed to refresh local node network status");
        }

        status.CheckedAt = DateTimeOffset.UtcNow;
        await session.StoreAsync(status, id, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
        return status;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await RefreshLocalAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                logger.LogWarning(ex, "Failed to publish local node network status");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }

    private async Task<string?> GetPublicIpAsync(string url, AddressFamily expectedFamily,
        CancellationToken cancellationToken) {
        try {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var value = (await client.GetStringAsync(url, cancellationToken)).Trim();
            return IPAddress.TryParse(value, out var address) && address.AddressFamily == expectedFamily
                ? address.ToString()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                   !cancellationToken.IsCancellationRequested) {
            logger.LogDebug(ex, "Public IP lookup failed for {Url}", url);
            return null;
        }
    }

    private static List<string> GetInternalAddresses() {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => !IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal)
            .Select(address => address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}