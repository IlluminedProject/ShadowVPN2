namespace ShadowVPN2.Data.SingBox.Models;

public interface IRuntimeManagedConfig {
    IReadOnlySet<string> RuntimeManagedProperties { get; }
}