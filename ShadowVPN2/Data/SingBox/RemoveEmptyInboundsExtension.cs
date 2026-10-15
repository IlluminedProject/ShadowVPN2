using ShadowVPN2.Data.SingBox.Models;

namespace ShadowVPN2.Data.SingBox;

public sealed class RemoveEmptyInboundsExtension : ISingBoxExtension {
    public Task ConfigureAsync(SingBoxContext context, CancellationToken cancellationToken) {
        context.Config.Inbounds.RemoveAll(static inbound => inbound is IConditionallyEnabledConfig {
            IsActuallyEnabled: false
        });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() {
        return ValueTask.CompletedTask;
    }
}