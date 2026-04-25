using Raven.Client.Documents;
using Raven.Embedded;
using Serilog;
using ShadowVPN2.Infrastructure;

namespace ShadowVPN2.Data;

public class RavenDbInitializer
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<RavenDbInitializer>();
    
    public const string DatabaseName = "ShadowVPN";
    public static IDocumentStore Initialize(string certificatePath)
    {
        var serverOptions = new ServerOptions
        {
            ServerUrl = "https://127.0.0.1:8888",
            DataDirectory = (DataUtils.DataFolder / "ravendb").ToString()
        };

        serverOptions.Secured(certificatePath);
        
        Logger.Information("Starting RavenDB server at {ServerUrl}. Data: {DataDirectory}", serverOptions.ServerUrl, serverOptions.DataDirectory);
        EmbeddedServer.Instance.StartServer(serverOptions);

        Logger.Information("RavenDB server started successfully");
        var documentStore = EmbeddedServer.Instance.GetDocumentStore(DatabaseName);
        Logger.Information("Connecting to RavenDB server");
        return documentStore;
    }
}
