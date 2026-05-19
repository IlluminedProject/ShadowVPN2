namespace ShadowVPN2.Data.Cluster;

public record AwgPeerInfo(string PublicKey, string MeshIp, string PublicAddress)
{
    public string PublicHost => PublicAddress.Split(":").First();
}