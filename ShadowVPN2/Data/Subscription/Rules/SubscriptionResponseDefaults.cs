using ShadowVPN2.Data.Subscription.Models.Rules;

namespace ShadowVPN2.Data.Subscription.Rules;

public static class SubscriptionResponseDefaults {
    public const SubscriptionResponseType Fallback = SubscriptionResponseType.Base64;

    private static string[] _mihomoPrefixes = new[] {
        "flclash", "rabbit", "flowvy", "murge", "mihomo", "prizrak-box", "koala-clash",
        "clash-verge", "clash-nyanpasu", "clashx meta", "clash-meta", "clash.meta"
    };
    public static readonly IReadOnlyList<SubscriptionResponseRule> Rules = CreateRules();

    private static IReadOnlyList<SubscriptionResponseRule> CreateRules() {
        var rules = new List<SubscriptionResponseRule>();
        rules.Add(new() {
            Id = "browser", Name = "Browser", ResponseType = SubscriptionResponseType.Browser,
            Conditions = [
                new SubscriptionHeaderCondition {
                    Header = "Accept", Operator = SubscriptionHeaderOperator.AcceptMediaType, Value = "text/html"
                }
            ]
        });
        foreach (var prefix in _mihomoPrefixes) {
            rules.Add(PrefixRule($"mihomo-{prefix}", $"Mihomo: {prefix}", SubscriptionResponseType.Mihomo, prefix));
        }

        rules.Add(PrefixRule("stash", "Stash", SubscriptionResponseType.Stash, "stash"));
        rules.Add(new SubscriptionResponseRule {
            Id = "singbox", Name = "Sing-box", ResponseType = SubscriptionResponseType.SingBox,
            Conditions = Array.AsReadOnly(new[] {
                new SubscriptionHeaderCondition {
                    Header = "User-Agent", Operator = SubscriptionHeaderOperator.Regex,
                    Value = "^(?:sfa|sfi|sfm|sft|karing|singbox)"
                }
            })
        });
        rules.Add(PrefixRule("clash", "Legacy Clash", SubscriptionResponseType.Clash, "clash"));
        rules.Add(PrefixRule("freeturn", "FreeTurn", SubscriptionResponseType.FreeTurn, "freeturn"));
        return rules.AsReadOnly();
    }

    private static SubscriptionResponseRule PrefixRule(string id, string name, SubscriptionResponseType responseType,
        string prefix) {
        return new SubscriptionResponseRule {
            Id = id, Name = name, ResponseType = responseType,
            Conditions = Array.AsReadOnly(new[] {
                new SubscriptionHeaderCondition {
                    Header = "User-Agent", Operator = SubscriptionHeaderOperator.StartsWith, Value = prefix
                }
            })
        };
    }
}