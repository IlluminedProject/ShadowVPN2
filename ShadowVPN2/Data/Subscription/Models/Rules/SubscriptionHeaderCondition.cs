using System.Text.RegularExpressions;

namespace ShadowVPN2.Data.Subscription.Models.Rules;

public sealed record SubscriptionHeaderCondition {
    public string Header { get; init; } = string.Empty;
    private SubscriptionHeaderOperator _operator;
    private string _value = string.Empty;
    private bool _caseSensitive;

    public SubscriptionHeaderOperator Operator {
        get => _operator;
        init { _operator = value; CompileRegex(); }
    }

    public string Value {
        get => _value;
        init { _value = value; CompileRegex(); }
    }

    public bool CaseSensitive {
        get => _caseSensitive;
        init { _caseSensitive = value; CompileRegex(); }
    }

    internal Regex? CompiledRegex { get; private set; }

    private void CompileRegex() {
        if (_operator != SubscriptionHeaderOperator.Regex) {
            CompiledRegex = null;
            return;
        }
        var options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        if (!_caseSensitive)
            options |= RegexOptions.IgnoreCase;
        CompiledRegex = new Regex(_value, options, TimeSpan.FromMilliseconds(100));
    }
}
