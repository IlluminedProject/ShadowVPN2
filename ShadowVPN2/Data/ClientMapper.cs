using ShadowVPN2.Contracts.Clients;
using ShadowVPN2.Entities.Proxy;

namespace ShadowVPN2.Data;

public static class ClientMapper {
    public static ClientResponse MapToResponse(EntityClient client, string? currentUserId = null,
        string? ownerDisplayName = null) {
        return new ClientResponse {
            Id = client.Id,
            SubscriptionId = client.SubscriptionId,
            Name = client.Name,
            AssignedIp = client.GetAssignedIp().ToString(),
            IsEnabled = client.IsEnabled,
            CreatedAt = client.CreatedAt,
            UserNumber = ParseUserNumber(client.Id),
            IsOwnedByCurrentUser = currentUserId is null || client.UserId == currentUserId,
            OwnerDisplayName = ownerDisplayName,
            WireGuard = client.WireGuard is not null
                ? new WireGuardClientSettingsResponse { Mtu = client.WireGuard.Mtu }
                : null,
            Hysteria2 = client.Hysteria2 is not null
                ? new Hysteria2ClientSettingsResponse { Password = client.Hysteria2.Password }
                : null
        };
    }

    private static int ParseUserNumber(string clientId) {
        return int.TryParse(clientId.Split('/').ElementAtOrDefault(1), out var userNumber) ? userNumber : 0;
    }
}