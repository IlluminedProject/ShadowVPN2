using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Serilog;
using TruePath;
using TruePath.SystemIo;

namespace ShadowVPN2.Infrastructure.Configurations;

public class LocalConfiguration
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<LocalConfiguration>();

    public static readonly AbsolutePath Path = DataUtils.DataFolder / "local";
    public static readonly AbsolutePath CertificatePfxPath = Path / "ca.pfx";
    public static readonly AbsolutePath CertificatePemPath = Path / "root-ca.crt";
    public static readonly AbsolutePath ConfigPath = Path / "config.json";

    public static async Task<LocalConfiguration> Initialize()
    {
        Logger.Information("Initializing local configuration at {Path}", Path);

        if (!Path.ExistsDirectory() ||
            !CertificatePfxPath.ExistsFile() || !CertificatePemPath.ExistsFile() ||
            !ConfigPath.ExistsFile())
        {
            Logger.Information("Local configuration or certificates not found. Starting first-run setup.");

            // TODO: Handle joining another node
            Path.CreateDirectory();
            var localConfiguration = new LocalConfiguration();
            localConfiguration.Save();

            Logger.Information("Generating new certificates for RavenDB cluster");
            var rootCa = RavenDbCertificates.GenerateRootCa();
            await CertificatePemPath.WriteAllTextAsync(rootCa.ExportCertificatePem());
            var (rsa, request) = RavenDbCertificates.GenerateCertificateRequest();
            var intermediateCertificate = RavenDbCertificates.SignCertificate(request, rootCa);
            var intermediateCertificateBytes = intermediateCertificate.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx);
            await CertificatePfxPath.WriteAllBytesAsync(intermediateCertificateBytes);
            Logger.Information("Certificates generated and saved successfully");
        }

        Logger.Debug("Loading Root CA and ensuring trust");
        var rootCaPem = X509CertificateLoader.LoadCertificateFromFile(CertificatePemPath.ToString());
        RavenDbCertificates.TrustCustomRootCa(rootCaPem);

        Logger.Information("Loading local configuration from {ConfigPath}", ConfigPath);
        var configText = await ConfigPath.ReadAllTextAsync();
        var config = JsonSerializer.Deserialize<LocalConfiguration>(configText, DataUtils.DefaultSerializerOptions)
               ?? throw new Exception($"Failed to deserialize local configuration from {ConfigPath}");

        Logger.Information("Local configuration initialized successfully");
        return config;
    }

    public void Save()
    {
        Logger.Information("Saving local configuration to {ConfigPath}", ConfigPath);
        var configText = JsonSerializer.Serialize(this, DataUtils.DefaultSerializerOptions);
        ConfigPath.WriteAllText(configText);
    }
}