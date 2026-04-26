namespace ShadowVPN2.Data;

public class NodeResponse
{
    public required string Id { get; set; }
    public required Guid NodeId { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public int Number { get; set; }
}
