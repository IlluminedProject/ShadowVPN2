namespace ShadowVPN2.Data.SingBox.Models;

public interface IRuntimeManagedInbound : IRuntimeManagedConfig {
    IReadOnlyList<RuntimeUserDto> RuntimeUsers { get; }
}