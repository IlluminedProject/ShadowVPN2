using Raven.Client.Documents;
using Raven.Embedded;
using ShadowVPN2.Infrastructure;

namespace ShadowVPN2.Data;

public static class RavenDbInitializer
{
    public const string DatabaseName = "ShadowVPN";
    public static IDocumentStore Initialize(string certificatePath)
    {
        var serverOptions = new ServerOptions
        {
            ServerUrl = "https://0.0.0.0:8888",
            DataDirectory = (DataUtils.DataFolder / "ravendb").ToString()
        };

        serverOptions.Secured(certificatePath);
        
        EmbeddedServer.Instance.StartServer(serverOptions);

        return EmbeddedServer.Instance.GetDocumentStore(DatabaseName);
    }
}
