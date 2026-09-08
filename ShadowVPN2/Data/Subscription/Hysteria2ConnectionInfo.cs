namespace ShadowVPN2.Data.Subscription;

public class Hysteria2ConnectionInfo : ProtocolConnectionInfo {
    public required string ServerAddress { get; set; }
    public int ServerPort { get; set; }
    public required string Password { get; set; }
    public string? ObfsType { get; set; }
    public string? ObfsPassword { get; set; }
    public string? Sni { get; set; }
    public string? PinSha256 { get; set; }

    public string CreateShareUrl(string clientName) {
        var queryParams = new Dictionary<string, string?> {
            ["insecure"] = "1",
            ["pinSHA256"] = PinSha256,
            ["obfs"] = ObfsType is null or "none" ? null : ObfsType,
            ["obfs-password"] = ObfsType is null or "none" ? null : ObfsPassword,
            ["sni"] = Sni,
            ["name"] = clientName
        };
        var query = string.Join("&", queryParams
            .Where(parameter => !string.IsNullOrEmpty(parameter.Value))
            .Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value!)}"));

        return $"hysteria2://{Uri.EscapeDataString(Password)}@{FormatHost(ServerAddress)}:{ServerPort}/?{query}";
    }

    private static string FormatHost(string host) {
        return host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
    }
}