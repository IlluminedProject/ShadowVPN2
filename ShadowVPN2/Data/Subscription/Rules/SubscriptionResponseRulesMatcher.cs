using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ShadowVPN2.Data.Subscription.Models.Rules;

namespace ShadowVPN2.Data.Subscription.Rules;

public static class SubscriptionResponseRulesMatcher {
    public static SubscriptionResponseType Select(IReadOnlyDictionary<string, string> headers) {
        return Select(SubscriptionResponseDefaults.Rules, SubscriptionResponseDefaults.Fallback, headers);
    }

    public static SubscriptionResponseType Select(IReadOnlyList<SubscriptionResponseRule> rules, SubscriptionResponseType fallback,
        IReadOnlyDictionary<string, string> headers) {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var rule in rules) {
            if (!rule.Enabled)
                continue;

            var matches = true;
            foreach (var condition in rule.Conditions) {
                if (TryGetHeader(headers, condition.Header, out var value) && Matches(condition, value))
                    continue;
                matches = false;
                break;
            }
            if (matches)
                return rule.ResponseType;
        }
        return fallback;
    }

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string value) {
        // Header names are insensitive even when the caller's dictionary comparer is sensitive.
        foreach (var header in headers) {
            if (!string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = header.Value;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool Matches(SubscriptionHeaderCondition condition, string value) {
        var comparison = condition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        switch (condition.Operator) {
            case SubscriptionHeaderOperator.Contains:
                return value.Contains(condition.Value, comparison);
            case SubscriptionHeaderOperator.StartsWith:
                return value.StartsWith(condition.Value, comparison);
            case SubscriptionHeaderOperator.Regex:
                try {
                    return condition.CompiledRegex!.IsMatch(value);
                }
                catch (RegexMatchTimeoutException) {
                    return false;
                }
            case SubscriptionHeaderOperator.AcceptMediaType:
                return AcceptsMediaType(value, condition.Value, comparison);
            default:
                throw new ArgumentOutOfRangeException(nameof(condition.Operator));
        }
    }

    private static bool AcceptsMediaType(string header, string expected, StringComparison comparison) {
        // Only an explicit media type selects a response; a generic */* request is not a browser signal.
        foreach (var item in header.Split(',')) {
            if (MediaTypeWithQualityHeaderValue.TryParse(item.Trim(), out var mediaType) &&
                mediaType.Quality.GetValueOrDefault(1) > 0 &&
                string.Equals(mediaType.MediaType, expected, comparison))
                return true;
        }
        return false;
    }
}
