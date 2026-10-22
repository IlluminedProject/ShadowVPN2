using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Json.Serialization.NewtonsoftJson;
using Serilog;
using ShadowVPN2.Infrastructure.RavenDb.Configurations;
using ShadowVPN2.Infrastructure.RavenDb.RavenEmbedded;
using ILogger = Serilog.ILogger;

namespace ShadowVPN2.Infrastructure.RavenDb;

public class RavenDbInitializer {
    public const string DatabaseName = "ShadowVPN";
    private static readonly ILogger _logger = Log.ForContext<RavenDbInitializer>();

    public static IDocumentStore Initialize(string certificatePath, int nodeNumber, RavenDbOptions options,
        string dataDirectory, bool isJoining, IEnumerable<string> trustedPeerCertificates,
        Action<JsonSerializer> customizeJsonSerializer) {
        var meshUrl = $"https://100.64.0.{nodeNumber + 10}:8888";
        var meshTcpUrl = $"tcp://100.64.0.{nodeNumber + 10}:38888";

        var serverOptions = new ServerOptions {
            ServerUrl = $"https://127.0.0.1:8888;{meshUrl}",
            DataDirectory = dataDirectory,
            ServerDirectory = options.ServerDirectory,
            CommandLineArgs = [
                $"--PublicServerUrl={meshUrl}",
                $"--ServerUrl.Cluster={meshUrl}",
                $"--ServerUrl.Tcp=tcp://127.0.0.1:38888;{meshTcpUrl}",
                $"--PublicServerUrl.Tcp={meshTcpUrl}",
                $"--PublicServerUrl.Tcp.Cluster={meshTcpUrl}"
            ]
        };
        serverOptions.BootstrapAdminCertificateThumbprints = [
            .. trustedPeerCertificates.Select(pem => {
                using var certificate = X509Certificate2.CreateFromPem(pem);
                return certificate.Thumbprint;
            })
        ];

        serverOptions.Secured(certificatePath);

        _logger.Information("Starting RavenDB server at {ServerUrl}. Data: {DataDirectory}", serverOptions.ServerUrl,
            serverOptions.DataDirectory);
        EmbeddedServer.Instance.StartServer(serverOptions);

        _logger.Information("RavenDB server started successfully");
        var databaseOptions = new DatabaseOptions(DatabaseName) {
            SkipCreatingDatabase = isJoining,
            LocalServerUrl = new Uri("https://127.0.0.1:8888"),
            Conventions = new DocumentConventions {
                DisableTopologyUpdates = true,
                Serialization = new NewtonsoftJsonSerializationConventions {
                    CustomizeJsonSerializer = customizeJsonSerializer
                }
            }
        };
        var documentStore = EmbeddedServer.Instance.GetDocumentStore(databaseOptions);

        _logger.Information("Connecting to RavenDB server");
        return documentStore;
    }
}