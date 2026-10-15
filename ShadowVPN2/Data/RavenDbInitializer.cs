using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Json.Serialization.NewtonsoftJson;
using Serilog;
using ShadowVPN2.Infrastructure;
using ShadowVPN2.Infrastructure.Configurations;
using ShadowVPN2.Infrastructure.RavenEmbedded;
using ILogger = Serilog.ILogger;

namespace ShadowVPN2.Data;

public class RavenDbInitializer {
    public const string DatabaseName = "ShadowVPN";
    private static readonly ILogger _logger = Log.ForContext<RavenDbInitializer>();

    public static IDocumentStore Initialize(string certificatePath, int nodeNumber, RavenDbOptions options,
        JoinStartupState joinState) {
        var meshUrl = $"https://100.64.0.{nodeNumber + 10}:8888";
        var meshTcpUrl = $"tcp://100.64.0.{nodeNumber + 10}:38888";

        var serverOptions = new ServerOptions {
            ServerUrl = $"https://127.0.0.1:8888;{meshUrl}",
            DataDirectory = (DataUtils.DataFolder / "ravendb").ToString(),
            ServerDirectory = options.ServerDirectory,
            CommandLineArgs = [
                $"--PublicServerUrl={meshUrl}",
                $"--ServerUrl.Cluster={meshUrl}",
                $"--ServerUrl.Tcp=tcp://127.0.0.1:38888;{meshTcpUrl}",
                $"--PublicServerUrl.Tcp={meshTcpUrl}",
                $"--PublicServerUrl.Tcp.Cluster={meshTcpUrl}"
            ]
        };
        if (joinState.IsJoining) {
            serverOptions.BootstrapAdminCertificateThumbprints = joinState.TemporaryTrustPeers.Select(peer => {
                using var certificate = X509Certificate2.CreateFromPem(peer.CertificatePem);
                return certificate.Thumbprint;
            }).ToArray();
        }

        serverOptions.Secured(certificatePath);

        _logger.Information("Starting RavenDB server at {ServerUrl}. Data: {DataDirectory}", serverOptions.ServerUrl,
            serverOptions.DataDirectory);
        EmbeddedServer.Instance.StartServer(serverOptions);

        _logger.Information("RavenDB server started successfully");
        var databaseOptions = new DatabaseOptions(DatabaseName) {
            SkipCreatingDatabase = joinState.IsJoining,
            LocalServerUrl = new Uri("https://127.0.0.1:8888"),
            Conventions = new DocumentConventions {
                DisableTopologyUpdates = true,
                Serialization = new NewtonsoftJsonSerializationConventions {
                    CustomizeJsonSerializer = serializer => {
                        serializer.Converters.Add(new PortBindingRavenJsonConverter());
                    }
                }
            }
        };
        var documentStore = EmbeddedServer.Instance.GetDocumentStore(databaseOptions);

        _logger.Information("Connecting to RavenDB server");
        return documentStore;
    }
}