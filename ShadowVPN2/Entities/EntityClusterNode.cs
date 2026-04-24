namespace ShadowVPN2.Entities;

public class EntityClusterNode
{
    /// <summary>
    /// Node ID
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The unique identifier of the physical node (from LocalConfiguration)
    /// </summary>
    public required Guid NodeId { get; set; }

    /// <summary>
    /// Node label
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Public IP/domain name of the node
    /// </summary>
    public required string Address { get; set; }
    
    /// <summary>
    /// Sequential number of the node
    /// </summary>
    public int Number => Id.EndsWith('|') ? 0 : int.Parse(Id.Split('/')[1]);
}