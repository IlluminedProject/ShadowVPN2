using ShadowVPN2.Entities.Auth;

namespace ShadowVPN2.Infrastructure.Authentication;

public sealed class DeviceAuthorizationNotificationService {
    private readonly Dictionary<string, List<Action<DeviceAuthorizationStatus>>> subscriptions = new();
    private readonly Lock syncRoot = new();

    public IDisposable Subscribe(string transactionId, Action<DeviceAuthorizationStatus> callback) {
        lock (syncRoot) {
            if (!subscriptions.TryGetValue(transactionId, out var callbacks)) {
                callbacks = [];
                subscriptions[transactionId] = callbacks;
            }

            callbacks.Add(callback);
        }

        return new Subscription(this, transactionId, callback);
    }

    public void Publish(string transactionId, DeviceAuthorizationStatus status) {
        Action<DeviceAuthorizationStatus>[] callbacks;
        lock (syncRoot) {
            callbacks = subscriptions.TryGetValue(transactionId, out var registered)
                ? registered.ToArray()
                : [];
        }

        foreach (var callback in callbacks)
            callback(status);
    }

    private void Unsubscribe(string transactionId, Action<DeviceAuthorizationStatus> callback) {
        lock (syncRoot) {
            if (!subscriptions.TryGetValue(transactionId, out var callbacks))
                return;

            callbacks.Remove(callback);
            if (callbacks.Count == 0)
                subscriptions.Remove(transactionId);
        }
    }

    private sealed class Subscription(
        DeviceAuthorizationNotificationService service,
        string transactionId,
        Action<DeviceAuthorizationStatus> callback) : IDisposable {
        private int disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                service.Unsubscribe(transactionId, callback);
        }
    }
}