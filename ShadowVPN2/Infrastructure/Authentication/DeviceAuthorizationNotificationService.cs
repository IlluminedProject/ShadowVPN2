using ShadowVPN2.Entities.Auth;

namespace ShadowVPN2.Infrastructure.Authentication;

public sealed class DeviceAuthorizationNotificationService {
    private readonly Dictionary<string, List<Action<DeviceAuthorizationStatus>>> _subscriptions = new();
    private readonly Lock _syncRoot = new();

    public IDisposable Subscribe(string transactionId, Action<DeviceAuthorizationStatus> callback) {
        lock (_syncRoot) {
            if (!_subscriptions.TryGetValue(transactionId, out var callbacks)) {
                callbacks = [];
                _subscriptions[transactionId] = callbacks;
            }

            callbacks.Add(callback);
        }

        return new Subscription(this, transactionId, callback);
    }

    public void Publish(string transactionId, DeviceAuthorizationStatus status) {
        Action<DeviceAuthorizationStatus>[] callbacks;
        lock (_syncRoot) {
            callbacks = _subscriptions.TryGetValue(transactionId, out var registered)
                ? registered.ToArray()
                : [];
        }

        foreach (var callback in callbacks)
            callback(status);
    }

    private void Unsubscribe(string transactionId, Action<DeviceAuthorizationStatus> callback) {
        lock (_syncRoot) {
            if (!_subscriptions.TryGetValue(transactionId, out var callbacks))
                return;

            callbacks.Remove(callback);
            if (callbacks.Count == 0)
                _subscriptions.Remove(transactionId);
        }
    }

    private sealed class Subscription(
        DeviceAuthorizationNotificationService service,
        string transactionId,
        Action<DeviceAuthorizationStatus> callback) : IDisposable {
        private int _disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                service.Unsubscribe(transactionId, callback);
        }
    }
}