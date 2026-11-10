using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components.Forms;
using ShadowVPN2.Contracts.Application;
using ShadowVPN2.Contracts.Networking;
using ShadowVPN2.Contracts.Nodes;
using ShadowVPN2.Contracts.Protocols;
using ShadowVPN2.Frontend.Pages;
using Xunit;

namespace ShadowVPN2.Frontend.Tests;

public sealed class NodesTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckDomainAsync_keeps_manual_result_after_reload_without_replacing_node_status(
        bool domainChanged) {
        var nodeId = Guid.NewGuid();
        var originalDomain = HostName.Parse("original.example.com");
        var requestedDomain = domainChanged ? HostName.Parse("updated.example.com") : originalDomain;
        var originalStatus = CreateStatus(originalDomain, DomainValidationState.Mismatch);
        var persistedStatus = CreateStatus(requestedDomain, DomainValidationState.NoRecords);
        var manualStatus = CreateStatus(requestedDomain, DomainValidationState.Valid);
        var originalNode = new NodeResponse {
            Id = "nodes/original", NodeId = nodeId, Name = "Node", Domain = originalDomain,
            DomainStatus = originalStatus
        };
        var reloadedNode = new NodeResponse {
            Id = originalNode.Id, NodeId = nodeId, Name = "Node", Domain = requestedDomain,
            DomainStatus = persistedStatus
        };
        var api = new NodesApi(reloadedNode, manualStatus);
        var request = new UpdateNodeRequest { Name = originalNode.Name, Domain = requestedDomain };
        var component = new Nodes();
        SetProperty(component, "NodesApi", api);
        SetField(component, "_editingNode", originalNode);
        SetField(component, "_domainRequest", request);
        SetField(component, "_domainEditContext", new EditContext(request));
        try {
            await InvokeAsync(component, "CheckDomainAsync");

            api.Calls.Should().Equal(domainChanged
                ? new[] { "update", "check", "nodes", "routes" }
                : new[] { "check", "nodes", "routes" });
            api.CheckedNodeId.Should().Be(nodeId);
            GetField(component, "_manualDomainCheck").Should().BeSameAs(manualStatus);
            GetField(component, "_editingNode").Should().BeSameAs(reloadedNode);
            originalNode.DomainStatus.Should().BeSameAs(originalStatus);
            reloadedNode.DomainStatus.Should().BeSameAs(persistedStatus);

            await InvokeAsync(component, "ReloadAsync");

            GetField(component, "_manualDomainCheck").Should().BeSameAs(manualStatus);
            reloadedNode.DomainStatus.Should().BeSameAs(persistedStatus);
        }
        finally {
            await component.DisposeAsync();
        }
    }

    [Fact]
    public void Realtime_update_refreshes_open_node_without_discarding_edits_or_manual_check() {
        var domain = HostName.Parse("node.example.com");
        var original = new NodeResponse {
            Id = "nodes/1", NodeId = Guid.NewGuid(), Name = "Node", Domain = domain
        };
        var updated = new NodeResponse {
            Id = original.Id, NodeId = original.NodeId, Name = original.Name, Domain = domain,
            DomainStatus = CreateStatus(domain, DomainValidationState.Valid)
        };
        var request = new UpdateNodeRequest { Name = "Unsaved name", Domain = HostName.Parse("edited.example.com") };
        var context = new EditContext(request);
        var manual = CreateStatus(domain, DomainValidationState.Mismatch);
        var component = new Nodes();
        SetField(component, "_editingNode", original);
        SetField(component, "_domainRequest", request);
        SetField(component, "_domainEditContext", context);
        SetField(component, "_manualDomainCheck", manual);

        typeof(Nodes).GetMethod("ApplyNodeUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, [new[] { updated }]);

        GetField(component, "_editingNode").Should().BeSameAs(updated);
        ((NodeResponse)GetField(component, "_editingNode")!).DomainStatus!.MatchesNode.Should().BeTrue();
        GetField(component, "_domainRequest").Should().BeSameAs(request);
        request.Name.Should().Be("Unsaved name");
        request.Domain.Should().Be(HostName.Parse("edited.example.com"));
        GetField(component, "_domainEditContext").Should().BeSameAs(context);
        GetField(component, "_manualDomainCheck").Should().BeSameAs(manual);
    }

    private static DomainCheckResponse CreateStatus(HostName domain, DomainValidationState state) {
        return new DomainCheckResponse {
            State = state,
            Domain = domain,
            ResolvedAddresses = [],
            CheckedAt = DateTimeOffset.UtcNow,
            ValidUntil = DateTimeOffset.UtcNow.AddMinutes(5)
        };
    }

    private static void SetProperty(Nodes component, string name, object value) {
        typeof(Nodes).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
    }

    private static void SetField(Nodes component, string name, object value) {
        typeof(Nodes).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
    }

    private static object? GetField(Nodes component, string name) {
        return typeof(Nodes).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component);
    }

    private static Task InvokeAsync(Nodes component, string name) {
        return (Task)typeof(Nodes).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component,
            null)!;
    }

    private sealed class NodesApi(NodeResponse node, DomainCheckResponse manualStatus) : INodeApplicationService {
        public List<string> Calls { get; } = [];
        public Guid? CheckedNodeId { get; private set; }

        public Task<IReadOnlyList<NodeResponse>> GetNodesAsync(CancellationToken cancellationToken = default) {
            Calls.Add("nodes");
            return Task.FromResult<IReadOnlyList<NodeResponse>>([node]);
        }

        public Task<IReadOnlyList<ProtocolDomainRouteResponse>> GetProtocolDomainRoutesAsync(
            CancellationToken cancellationToken = default) {
            Calls.Add("routes");
            return Task.FromResult<IReadOnlyList<ProtocolDomainRouteResponse>>([]);
        }

        public Task<NodeResponse> UpdateAsync(Guid nodeId, UpdateNodeRequest request,
            CancellationToken cancellationToken = default) {
            Calls.Add("update");
            return Task.FromResult(node);
        }

        public Task<DomainCheckResponse> CheckDomainAsync(Guid nodeId,
            CancellationToken cancellationToken = default) {
            Calls.Add("check");
            CheckedNodeId = nodeId;
            return Task.FromResult(manualStatus);
        }

        public Task DeleteAsync(Guid nodeId, CancellationToken cancellationToken = default) {
            throw new NotSupportedException();
        }

        public Task<NodeCertificateDomainsResponse> GetCertificateDomainsAsync(Guid nodeId,
            CancellationToken cancellationToken = default) {
            throw new NotSupportedException();
        }
    }
}