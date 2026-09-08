namespace ShadowVPN2.Infrastructure.Authentication;

public class ActionObserver<T>(Action<T> onNext, Action<Exception>? onError = null, Action? onCompleted = null)
    : IObserver<T> {
    public void OnCompleted() {
        onCompleted?.Invoke();
    }

    public void OnError(Exception error) {
        onError?.Invoke(error);
    }

    public void OnNext(T value) {
        onNext(value);
    }
}