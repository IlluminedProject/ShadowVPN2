using ShadowVPN2.Data.Subscription.Models.Rules;

namespace ShadowVPN2.Infrastructure.Middleware;

internal static class SubscriptionFormatParser {
    public static SubscriptionResponseType? Parse(string id) => id.ToLowerInvariant() switch {
        "base64" => SubscriptionResponseType.Base64,
        "mihomo" => SubscriptionResponseType.Mihomo,
        "mihomo-provider" => SubscriptionResponseType.MihomoProvider,
        "freeturn" => SubscriptionResponseType.FreeTurn,
        "stash" => SubscriptionResponseType.Stash,
        "clash" => SubscriptionResponseType.Clash,
        "singbox" => SubscriptionResponseType.SingBox,
        "json" or "v2ray-json" or "xray-json" => SubscriptionResponseType.XrayJson,
        _ => null
    };
}
