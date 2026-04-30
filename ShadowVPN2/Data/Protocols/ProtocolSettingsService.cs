using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Entities;
using SessionOptions = Raven.Client.Documents.Session.SessionOptions;

namespace ShadowVPN2.Data.Protocols;

public class ProtocolSettingsService(IDocumentStore documentStore, ILogger<ProtocolSettingsService> logger)
{
    public async Task<IReadOnlyList<ProtocolGlobalSettings>> GetConfigurationAsync()
    {
        using var session = documentStore.OpenAsyncSession();
        return await session.Query<ProtocolGlobalSettings>().ToListAsync();
    }

    public async Task<ProtocolsSettingsResponse> GetSettingsAsync()
    {
        var protocols = await GetConfigurationAsync();
        return new ProtocolsSettingsResponse
        {
            Protocols = protocols.ToList()
        };
    }

    public async Task UpdateSettingsAsync(UpdateProtocolsSettingsRequest request)
    {
        using var session = documentStore.OpenAsyncSession(new SessionOptions
        {
            TransactionMode = TransactionMode.ClusterWide
        });

        var existingProtocols = await session.Query<ProtocolGlobalSettings>().ToListAsync();
        var incomingIds = request.Protocols.Select(p => p.Id).Where(id => id != null).ToHashSet();

        // 1. Delete protocols that are not in the request
        foreach (var existing in existingProtocols)
            if (!incomingIds.Contains(existing.Id))
                session.Delete(existing.Id);

        // 2. Upsert protocols from the request
        foreach (var incoming in request.Protocols)
            // If ID is null, RavenDB will assign a new one (e.g. Protocols/...)
            await session.StoreAsync(incoming);

        await session.SaveChangesAsync();
        logger.LogInformation("Global protocol settings synchronized. Count: {Count}", request.Protocols.Count);
    }
}