namespace ShadowVPN2.Data.SingBox.Models;

public interface IConditionallyEnabledConfig {
    bool IsActuallyEnabled { get; }
}