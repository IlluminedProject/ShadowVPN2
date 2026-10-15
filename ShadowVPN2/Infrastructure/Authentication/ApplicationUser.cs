namespace ShadowVPN2.Infrastructure.Authentication;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : AdvancedIdentityUser {
    public int UserNumber { get; set; }

    /// <summary>OIDC preferred_username or name; populated on sign-in when absent.</summary>
    public string? DisplayName { get; set; }
}