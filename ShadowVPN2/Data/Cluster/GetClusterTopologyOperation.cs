using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Client.ServerWide.Commands;
using Raven.Client.ServerWide.Operations;
using Sparrow.Json;

namespace ShadowVPN2.Data.Cluster;

public sealed class GetClusterTopologyOperation : IServerOperation<ClusterTopologyResponse> {
    public RavenCommand<ClusterTopologyResponse> GetCommand(DocumentConventions conventions,
        JsonOperationContext context) {
        return new GetClusterTopologyCommand();
    }
}