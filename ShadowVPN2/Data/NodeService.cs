using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class NodeService : IDisposable
{
    private readonly IDocumentStore _documentStore;
    private readonly ILogger<NodeService> _logger;
    private IDisposable? _changesSubscription;

    public event Func<Task>? NodesChanged;

    public NodeService(IDocumentStore documentStore, ILogger<NodeService> logger)
    {
        _documentStore = documentStore;
        _logger = logger;

        InitializeChangesSubscription();
    }

    private void InitializeChangesSubscription()
    {
        try
        {
            _changesSubscription = _documentStore.Changes()
                .ForDocumentsInCollection<EntityClusterNode>()
                .Subscribe(new ActionObserver<DocumentChange>(change =>
                {
                    _logger.LogInformation("Nodes collection changed: {Type} for {Id}", change.Type, change.Id);
                    NotifyNodesChanged();
                }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RavenDB changes subscription for nodes");
        }
    }

    private void NotifyNodesChanged()
    {
        _ = Task.Run(async () =>
        {
            if (NodesChanged != null)
            {
                await NodesChanged.Invoke();
            }
        });
    }

    public async Task<IReadOnlyList<EntityClusterNode>> GetNodesAsync()
    {
        using var session = _documentStore.OpenAsyncSession();
        var nodes = await session.Query<EntityClusterNode>().ToListAsync();
        return nodes.AsReadOnly();
    }

    public void Dispose()
    {
        _changesSubscription?.Dispose();
    }
}
