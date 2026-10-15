using System.Globalization;
using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Networking;

/// <summary>Describes a listener and an optional port range starting at that listener.</summary>
[JsonConverter(typeof(PortBindingJsonConverter))]
public readonly record struct PortBinding {
    [JsonConstructor]
    public PortBinding(int listenPort, int? portRangeEnd = null) {
        ValidatePort(listenPort, nameof(listenPort));
        if (portRangeEnd is { } end) {
            ValidatePort(end, nameof(portRangeEnd));
            ArgumentOutOfRangeException.ThrowIfLessThan(end, listenPort, nameof(portRangeEnd));
        }

        ListenPort = listenPort;
        PortRangeEnd = portRangeEnd == listenPort ? null : portRangeEnd;
    }

    public int ListenPort { get; }
    public int? PortRangeEnd { get; }

    public bool Contains(int port) {
        return port >= ListenPort && port <= (PortRangeEnd ?? ListenPort);
    }

    public bool Overlaps(PortBinding other) {
        return ListenPort <= (other.PortRangeEnd ?? other.ListenPort) &&
               other.ListenPort <= (PortRangeEnd ?? ListenPort);
    }

    public static bool TryParse(string? value, out PortBinding binding) {
        binding = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var span = value.AsSpan().Trim();
        var separator = span.IndexOf('-');
        var startSpan = separator < 0 ? span : span[..separator].Trim();
        var endSpan = separator < 0 ? span : span[(separator + 1)..].Trim();
        if (!int.TryParse(startSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(endSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var end) ||
            start < 1 || end > 65535 || start > end)
            return false;

        binding = new PortBinding(start, end);
        return true;
    }

    public override string ToString() {
        return PortRangeEnd is { } end
            ? string.Create(CultureInfo.InvariantCulture, $"{ListenPort}-{end}")
            : ListenPort.ToString(CultureInfo.InvariantCulture);
    }

    private static void ValidatePort(int port, string parameterName) {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535, parameterName);
    }
}