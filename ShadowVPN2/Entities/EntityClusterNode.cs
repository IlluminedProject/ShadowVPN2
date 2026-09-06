using ShadowVPN2.Entities.Base;

namespace ShadowVPN2.Entities;

public class EntityClusterNode : IEntityId {
    /// <summary>
    /// The unique identifier of the physical node (from LocalConfiguration)
    /// </summary>
    public required Guid NodeId { get; set; }

    /// <summary>
    /// Node label
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Public DNS name of the node. A node without a domain is addressed by its observed public IP.
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    ///     AmneziaWG public key for mesh networking (set when node completes join)
    /// </summary>
    public string? AwgPublicKey { get; set; }

    /// <summary>
    ///     One-time secret for pending node join. Null after join completes.
    /// </summary>
    public Guid? JoinSecret { get; set; }

    /// <summary>
    /// Sequential number of the node
    /// </summary>
    public int Number => Id.EndsWith('|') ? 0 : int.Parse(Id.Split('/')[1]);

    /// <summary>
    ///     AWG mesh IP derived from node number
    /// </summary>
    public string AwgMeshIp => $"100.64.0.{Number + 10}";

    /// <summary>
    ///     Node ID
    /// </summary>
    public required string Id { get; init; }
}