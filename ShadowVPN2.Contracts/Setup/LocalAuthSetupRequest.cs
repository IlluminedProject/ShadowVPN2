namespace ShadowVPN2.Contracts.Setup;

public sealed class LocalAuthSetupRequest {
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}