using System.Text;

namespace ShadowVPN2.Contracts.Subscriptions;

public sealed class ConfigBuilder {
    private readonly StringBuilder _config = new();

    public ConfigBuilder AddSection(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_config.Length > 0)
            _config.AppendLine();
        _config.Append('[').Append(name).AppendLine("]");
        return this;
    }

    public ConfigBuilder Add(string name, object? value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _config.Append(name).Append(" = ").AppendLine(value.ToString());
        return this;
    }

    public ConfigBuilder AddOptional(string name, object? value) {
        return value is null || string.IsNullOrWhiteSpace(value.ToString()) ? this : Add(name, value);
    }

    public string Build() {
        return _config.ToString();
    }
}