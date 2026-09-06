namespace ShadowVPN2.Components.Pages.ConnectionViews;

public sealed record ProtocolSubscriptionDescriptor(
    string Protocol,
    Type ComponentType) {
    public static IReadOnlyList<ProtocolSubscriptionDescriptor> All { get; } = [
        new("Hysteria2", typeof(Hysteria2ProtocolView))
    ];
}