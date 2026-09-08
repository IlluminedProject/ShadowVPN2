using Raven.Client.Documents;
using Raven.Embedded;
using Serilog;
using ShadowVPN2.Infrastructure;
using ILogger = Serilog.ILogger;

namespace ShadowVPN2.Data;

public class RavenDbInitializer {
    public const string DatabaseName = "ShadowVPN";
    private static readonly ILogger _logger = Log.ForContext<RavenDbInitializer>();

    public static IDocumentStore Initialize(string certificatePath, int nodeNumber) {
        var meshUrl = $"https://100.64.0.{nodeNumber + 10}:8888";

        var serverOptions = new ServerOptions {
            ServerUrl = "https://0.0.0.0:8888",
            DataDirectory = (DataUtils.DataFolder / "ravendb").ToString(),
            CommandLineArgs = ["--PublicServerUrl=https://127.0.0.1:8888", $"--ServerUrl.Cluster={meshUrl}"]
        };

        serverOptions.Secured(certificatePath);

        _logger.Information("Starting RavenDB server at {ServerUrl}. Data: {DataDirectory}", serverOptions.ServerUrl,
            serverOptions.DataDirectory);
        EmbeddedServer.Instance.StartServer(serverOptions);

        _logger.Information("RavenDB server started successfully");
        var databaseOptions = new DatabaseOptions(DatabaseName);
        var documentStore = EmbeddedServer.Instance.GetDocumentStore(databaseOptions);

        _logger.Information("Connecting to RavenDB server");
        return documentStore;
    }
}