namespace ShadowVPN2.Contracts.Auth;

public sealed class AccountProfileResponse {
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool HasPassword { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public bool HasAuthenticator { get; init; }
    public int RecoveryCodesLeft { get; init; }
    public bool IsTwoFactorClientRemembered { get; init; }
}

public sealed class PasskeyResponse {
    public required string Id { get; init; }
    public string? Name { get; init; }
}

public sealed class ProfileRequest {
    public string? PhoneNumber { get; init; }
}

public sealed class EmailRequest {
    public required string Email { get; init; }
}

public sealed class PasswordRequest {
    public string? CurrentPassword { get; init; }
    public required string NewPassword { get; init; }
}

public sealed class AuthenticatorCodeRequest {
    public required string Code { get; init; }
}

public sealed class PasskeyRenameRequest {
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class PasskeyDeleteRequest {
    public required string Id { get; init; }
}

public sealed class DeleteAccountRequest {
    public string? Password { get; init; }
}