using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using ShadowVPN2.Entities.Proxy;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class ClientService(IDocumentStore documentStore, ILogger<ClientService> logger) : IDisposable
{
    private IDisposable? _changesSubscription;
    private readonly HashSet<ClientSubscription> _subscriptions = new();
    private readonly Lock _lock = new();
    private CancellationTokenSource? _disposalCts;

    public async Task<IReadOnlyList<EntityClient>> GetClientsAsync(ApplicationUser user,
        CancellationToken ct = default)
    {
        using var session = documentStore.OpenAsyncSession();
        return await session.Query<EntityClient>()
            .Where(c => c.UserId == user.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EntityClient>> GetClientsByUserNumberAsync(int userNumber,
        CancellationToken ct = default)
    {
        using var session = documentStore.OpenAsyncSession();
        // Since the user number is part of the ID: Clients/{userNumber}/...
        // We can use a starts-with query on the ID.
        var results = await session.Advanced.LoadStartingWithAsync<EntityClient>($"Clients/{userNumber}/", null, 0, int.MaxValue, null, null, ct);
        return results.ToList().AsReadOnly();
    }

    public async Task<EntityClient?> GetClientAsync(string clientId, string userId,
        CancellationToken ct = default)
    {
        using var session = documentStore.OpenAsyncSession();
        var client = await session.LoadAsync<EntityClient>(clientId, ct);
        return client?.UserId == userId ? client : null;
    }

    public async Task<EntityClient> AddClientAsync(ApplicationUser user, string name,
        WireGuardClientSettings? wireGuard = null, CancellationToken ct = default)
    {
        if (user.UserNumber == 0)
            throw new InvalidOperationException($"User {user.Id} has no UserNumber assigned");

        using var session = documentStore.OpenAsyncSession();

        var existingClients = await session.Advanced.LoadStartingWithAsync<EntityClient>(
            $"Clients/{user.UserNumber}/", null, 0, int.MaxValue, null, null, ct);

        var usedNumbers = existingClients
            .Select(c => c.Id.Split('/'))
            .Where(parts => parts.Length == 3)
            .Select(parts => int.TryParse(parts[2], out var n) ? n : 0)
            .Where(n => n > 0)
            .ToHashSet();

        var nextNumber = 1;
        while (usedNumbers.Contains(nextNumber))
        {
            nextNumber++;
        }

        if (nextNumber > 254)
        {
            throw new InvalidOperationException("Maximum number of clients (254) reached for this user.");
        }

        var clientId = $"Clients/{user.UserNumber}/{nextNumber}";

        var client = new EntityClient
        {
            Id = clientId,
            UserId = user.Id!,
            Name = name,
            WireGuard = wireGuard,
        };

        // Pass string.Empty as change vector to assert the document does not exist
        await session.StoreAsync(client, string.Empty, clientId, ct);
        await session.SaveChangesAsync(ct);

        logger.LogInformation("Client {ClientId} created for user {UserId}", client.Id, user.Id);
        return client;
    }

    public async Task<EntityClient?> UpdateClientAsync(string clientId, string userId, string name, bool isEnabled,
        WireGuardClientSettings? wireGuard, CancellationToken ct = default)
    {
        using var session = documentStore.OpenAsyncSession();
        var client = await session.LoadAsync<EntityClient>(clientId, ct);
        if (client is null || client.UserId != userId)
            return null;

        client.Name = name;
        client.IsEnabled = isEnabled;
        client.WireGuard = wireGuard;

        await session.SaveChangesAsync(ct);
        return client;
    }

    public async Task<bool> DeleteClientAsync(string clientId, string userId, CancellationToken ct = default)
    {
        using var session = documentStore.OpenAsyncSession();
        var client = await session.LoadAsync<EntityClient>(clientId, ct);
        if (client is null || client.UserId != userId)
            return false;

        session.Delete(client);

        var stateId = clientId.Replace("Clients/", "ClientStates/");
        var state = await session.LoadAsync<EntityClientState>(stateId, ct);
        if (state is not null)
            session.Delete(state);

        await session.SaveChangesAsync(ct);
        logger.LogInformation("Client {ClientId} deleted", clientId);
        return true;
    }

    public async Task<ClientSubscription> SubscribeAsync(ApplicationUser user, Func<IReadOnlyList<EntityClient>, Task>? onUpdate = null)
    {
        var subscription = new ClientSubscription(this, user.Id!, user.UserNumber);
        if (onUpdate != null)
        {
            subscription.ClientsUpdated += onUpdate;
        }

        lock (_lock)
        {
            _subscriptions.Add(subscription);

            if (_disposalCts != null)
            {
                _disposalCts.Cancel();
                _disposalCts.Dispose();
                _disposalCts = null;
            }

            if (_subscriptions.Count == 1 && _changesSubscription == null)
            {
                InitializeChangesSubscription();
            }
        }

        return await Task.FromResult(subscription);
    }

    private void Unsubscribe(ClientSubscription subscription)
    {
        lock (_lock)
        {
            if (_subscriptions.Remove(subscription) && _subscriptions.Count == 0)
            {
                if (_changesSubscription != null)
                {
                    _disposalCts?.Cancel();
                    _disposalCts?.Dispose();
                    _disposalCts = new CancellationTokenSource();
                    var token = _disposalCts.Token;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), token);
                            lock (_lock)
                            {
                                if (!token.IsCancellationRequested && _subscriptions.Count == 0 && _changesSubscription != null)
                                {
                                    _changesSubscription.Dispose();
                                    _changesSubscription = null;
                                }
                            }
                        }
                        catch (TaskCanceledException) { }
                    }, CancellationToken.None);
                }
            }
        }
    }

    private void InitializeChangesSubscription()
    {
        try
        {
            _changesSubscription = documentStore.Changes()
                .ForDocumentsInCollection<EntityClient>()
                .Subscribe(new ActionObserver<DocumentChange>(change =>
                {
                    var parts = change.Id.Split('/');
                    if (parts.Length > 1 && int.TryParse(parts[1], out var userNumber))
                    {
                        NotifyClientsChanged(userNumber);
                    }
                }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize RavenDB changes subscription for VPN clients");
        }
    }

    private void NotifyClientsChanged(int userNumber)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var clients = await GetClientsByUserNumberAsync(userNumber);

                List<ClientSubscription> targets;
                lock (_lock)
                {
                    targets = _subscriptions.Where(s => s.UserNumber == userNumber).ToList();
                }

                if (targets.Count > 0)
                {
                    await Task.WhenAll(targets.Select(t => t.NotifyAsync(clients)));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error notifying client changes for user {UserNumber}", userNumber);
            }
        });
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposalCts?.Cancel();
            _disposalCts?.Dispose();
            _disposalCts = null;
            _changesSubscription?.Dispose();
            _changesSubscription = null;
            _subscriptions.Clear();
        }
    }

    public class ClientSubscription(ClientService service, string userId, int userNumber) : IDisposable
    {
        private bool _disposed;
        public string UserId { get; } = userId;
        public int UserNumber { get; } = userNumber;

        public event Func<IReadOnlyList<EntityClient>, Task>? ClientsUpdated;

        public async Task<IReadOnlyList<EntityClient>> GetCurrentClientsAsync()
        {
            return await service.GetClientsByUserNumberAsync(UserNumber);
        }

        public async Task NotifyAsync(IReadOnlyList<EntityClient> clients)
        {
            if (!_disposed && ClientsUpdated != null)
            {
                await ClientsUpdated.Invoke(clients);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                service.Unsubscribe(this);
                _disposed = true;
            }
        }
    }
}
