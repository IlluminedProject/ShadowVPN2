namespace ShadowVPN2.Data.SingBox;

public interface ISingBoxExtension : IAsyncDisposable {
    Task ConfigureAsync(SingBoxContext context, CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }

    Task ApplyAsync(SingBoxContext context, CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}