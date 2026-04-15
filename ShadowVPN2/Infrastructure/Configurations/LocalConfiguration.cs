using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using TruePath;
using TruePath.SystemIo;

namespace ShadowVPN2.Infrastructure.Configurations;

public class LocalConfiguration
{
    public static readonly AbsolutePath Path = DataUtils.DataFolder / "local";
    public static readonly AbsolutePath CertificatePfxPath = Path / "ca.pfx";
    public static readonly AbsolutePath CertificatePemPath = Path / "root-ca.crt";
    public static readonly AbsolutePath ConfigPath = Path / "config.json";

    public static async Task<LocalConfiguration> Initialize()
    {
        if (!Path.ExistsDirectory() ||
            !CertificatePfxPath.ExistsFile() || !CertificatePemPath.ExistsFile() ||
            !ConfigPath.ExistsFile())
        {
            // TODO: Handle joining another node
            Path.CreateDirectory();
            var localConfiguration = new LocalConfiguration();
            localConfiguration.Save();

            var rootCa = RavenDbCertificates.GenerateRootCa();
            await CertificatePemPath.WriteAllTextAsync(rootCa.ExportCertificatePem());
            var (rsa, request) = RavenDbCertificates.GenerateCertificateRequest();
            var intermediateCertificate = RavenDbCertificates.SignCertificate(request, rootCa);
            var intermediateCertificateBytes = intermediateCertificate.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx);
            await CertificatePfxPath.WriteAllBytesAsync(intermediateCertificateBytes);
        }

        var rootCaPem = X509CertificateLoader.LoadCertificateFromFile(CertificatePemPath.ToString());
        RavenDbCertificates.TrustCustomRootCa(rootCaPem);

        var configText = await ConfigPath.ReadAllTextAsync();
        return JsonSerializer.Deserialize<LocalConfiguration>(configText, DataUtils.DefaultSerializerOptions)
               ?? throw new Exception($"Failed to deserialize local configuration from {ConfigPath}");
    }

    public void Save()
    {
        var configText = JsonSerializer.Serialize(this, DataUtils.DefaultSerializerOptions);
        ConfigPath.WriteAllText(configText);
    }
}