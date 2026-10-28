using ShadowVPN2.Data.Subscription;
using ShadowVPN2.Data.Subscription.Models.Rules;
using ShadowVPN2.Data.Subscription.Rules;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class SubscriptionResponseRulesTests {
    [Theory]
    [InlineData("MiHoMo/1.19", "*/*", SubscriptionResponseType.Mihomo)]
    [InlineData("FlClash X/1.0", "*/*", SubscriptionResponseType.Mihomo)]
    [InlineData("clash-meta", "*/*", SubscriptionResponseType.Mihomo)]
    [InlineData("v2rayNG/1.9", "*/*", SubscriptionResponseType.Base64)]
    [InlineData("unknown", "text/html", SubscriptionResponseType.Browser)]
    [InlineData("mihomo", "text/html;q=0, */*;q=1", SubscriptionResponseType.Mihomo)]
    [InlineData("unknown", "*/*", SubscriptionResponseType.Base64)]
    [InlineData("stash/2", "*/*", SubscriptionResponseType.Stash)]
    [InlineData("singbox/1", "*/*", SubscriptionResponseType.SingBox)]
    [InlineData("Clash for Windows", "*/*", SubscriptionResponseType.Clash)]
    [InlineData("unknown", "application/xhtml+xml, text/html;q=0.9", SubscriptionResponseType.Browser)]
    public void Defaults_select_supported_format(string userAgent, string accept, SubscriptionResponseType expected) {
        Assert.Equal(expected, SubscriptionResponseRulesMatcher.Select(
            new Dictionary<string, string> { ["user-agent"] = userAgent, ["accept"] = accept }));
    }

    [Fact]
    public void Rules_are_ordered_disabled_rules_are_skipped_and_conditions_are_anded() {
        IReadOnlyList<SubscriptionResponseRule> rules = [
            new() { Enabled = false, ResponseType = SubscriptionResponseType.NotFound },
            new() { ResponseType = SubscriptionResponseType.Block, Conditions = [
                new() { Header = "User-Agent", Operator = SubscriptionHeaderOperator.StartsWith, Value = "test" },
                new() { Header = "X-Client", Operator = SubscriptionHeaderOperator.Contains, Value = "denied" }
            ] },
            new() { ResponseType = SubscriptionResponseType.Mihomo, Conditions = [
                new() { Header = "User-Agent", Operator = SubscriptionHeaderOperator.Regex, Value = "^test/" }
            ] },
            new() { ResponseType = SubscriptionResponseType.FreeTurn }
        ];
        var headers = new Dictionary<string, string> { ["User-Agent"] = "TEST/1" };
        Assert.Equal(SubscriptionResponseType.Mihomo, SubscriptionResponseRulesMatcher.Select(rules, SubscriptionResponseType.Base64, headers));
        headers["X-Client"] = "DENIED";
        Assert.Equal(SubscriptionResponseType.Block, SubscriptionResponseRulesMatcher.Select(rules, SubscriptionResponseType.Base64, headers));
        headers.Clear();
        Assert.Equal(SubscriptionResponseType.FreeTurn, SubscriptionResponseRulesMatcher.Select(rules, SubscriptionResponseType.Base64, headers));
        Assert.Equal(SubscriptionResponseType.Base64, SubscriptionResponseRulesMatcher.Select([], SubscriptionResponseType.Base64, headers));
    }
}
