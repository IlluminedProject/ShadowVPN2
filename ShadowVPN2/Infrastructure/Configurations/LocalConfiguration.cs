using System.Text.Json;
using Serilog;
using TruePath;
using TruePath.SystemIo;
using ILogger = Serilog.ILogger;

namespace ShadowVPN2.Infrastructure.Configurations;

public class LocalConfiguration {
    private static readonly ILogger _logger = Log.ForContext<LocalConfiguration>();

    public static readonly AbsolutePath Path = DataUtils.DataFolder / "local";
    public static readonly AbsolutePath CertificatePfxPath = Path / "node.pfx";
    public static readonly AbsolutePath CertificatePemPath = Path / "node.crt";
    public static readonly AbsolutePath SetupPendingPath = Path / "setup-pending";
    public static readonly AbsolutePath ConfigPath = Path / "config.json";

    /// <summary>
    ///     The unique identifier of the current node.
    /// </summary>
    public Guid NodeId { get; set; } = Guid.NewGuid();

    /// <summary>
    ///     AmneziaWG private key for mesh networking. Generated once per node, never transmitted.
    /// </summary>
    public string? AwgPrivateKey { get; set; }

    /// <summary>
    ///     Sequential number of the node (1 for the first node). Used to determine AWG mesh IP.
    /// </summary>
    public int NodeNumber { get; set; }


    public void CopyFrom(LocalConfiguration configuration) {
        NodeId = configuration.NodeId;
        AwgPrivateKey = configuration.AwgPrivateKey;
        NodeNumber = configuration.NodeNumber;
    }

    public static async Task<LocalConfiguration> LoadAsync() {
        _logger.Information("Initializing local configuration at {Path}", Path);
        if (!File.Exists(CertificatePfxPath.Value)) {
            throw new InvalidOperationException(
                "This data directory uses the retired CA certificate format. Initialize a fresh cluster and rejoin nodes with new join tokens; automatic CA migration is unsupported.");
        }

        _logger.Information("Loading local configuration from {ConfigPath}", ConfigPath);
        var configText = await ConfigPath.ReadAllTextAsync();
        var config = JsonSerializer.Deserialize<LocalConfiguration>(configText, DataUtils.DefaultSerializerOptions)
                     ?? throw new Exception($"Failed to deserialize local configuration from {ConfigPath}");
        config.Save();

        _logger.Information("Local configuration initialized successfully");
        return config;
    }

    public void Save() {
        _logger.Information("Saving local configuration to {ConfigPath}", ConfigPath);
        var configText = JsonSerializer.Serialize(this, DataUtils.DefaultSerializerOptions);
        ConfigPath.WriteAllText(configText);
    }
}