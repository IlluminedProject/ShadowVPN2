using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Serilog;
using TruePath;
using TruePath.SystemIo;
using ILogger = Serilog.ILogger;

namespace ShadowVPN2.Infrastructure.Configurations;

public class LocalConfiguration {
    private static readonly ILogger _logger = Log.ForContext<LocalConfiguration>();

    public static readonly AbsolutePath Path = DataUtils.DataFolder / "local";
    public static readonly AbsolutePath CertificatePfxPath = Path / "ca.pfx";
    public static readonly AbsolutePath CertificatePemPath = Path / "root-ca.crt";
    public static readonly AbsolutePath RootCaPfxPath = Path / "root-ca.pfx";
    public static readonly AbsolutePath ConfigPath = Path / "config.json";

    /// <summary>
    /// The unique identifier of the current node.
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
        _logger.Debug("Loading Root CA and ensuring trust");
        var rootCaPem = X509CertificateLoader.LoadCertificateFromFile(CertificatePemPath.ToString());
        RavenDbCertificates.TrustCustomRootCa(rootCaPem);

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