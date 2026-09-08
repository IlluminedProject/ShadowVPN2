namespace ShadowVPN2.Entities.Auth;

public enum DeviceAuthorizationStatus {
    Pending,
    Processing,
    Completed,
    Consumed,
    Expired,
    Denied,
    Failed
}