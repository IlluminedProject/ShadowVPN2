using AwesomeAssertions;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Infrastructure.Authentication;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class DynamicOpenIddictClientRegistrationStoreTests {
    [Fact]
    public void AddOrUpdate_should_keep_the_registration_until_it_is_removed() {
        var optionsCache = new OptionsCache<OpenIddictClientOptions>();
        var store = new DynamicOpenIddictClientRegistrationStore(optionsCache);
        var provider = new OidcAuthProvider {
            SchemeName = "OIDC",
            DisplayName = "Test provider",
            Authority = "https://identity.example.com",
            ClientId = "web-client",
            Scopes = "openid email"
        };

        store.AddOrUpdate(provider);

        var registration = store.GetRegistrations().Should().ContainSingle().Which;
        registration.RegistrationId.Should().Be("OIDC");
        registration.ClientId.Should().Be("web-client");
        registration.Scopes.Should().Contain("openid");
        registration.Scopes.Should().Contain("email");

        store.Remove("OIDC");

        store.GetRegistrations().Should().BeEmpty();
    }

    [Fact]
    public void AddOrUpdate_should_reject_non_http_authority() {
        var store = new DynamicOpenIddictClientRegistrationStore(
            new OptionsCache<OpenIddictClientOptions>());
        var provider = new OidcAuthProvider {
            SchemeName = "OIDC",
            Authority = "not-a-uri"
        };

        var action = () => store.AddOrUpdate(provider);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddOrUpdate_should_use_identity_scopes_when_scopes_are_empty() {
        var store = new DynamicOpenIddictClientRegistrationStore(
            new OptionsCache<OpenIddictClientOptions>());
        var provider = new OidcAuthProvider {
            SchemeName = "OIDC",
            Authority = "https://identity.example.com",
            Scopes = " "
        };

        store.AddOrUpdate(provider);

        var scopes = store.GetRegistrations().Should().ContainSingle().Which.Scopes;
        scopes.Should().Contain("openid");
        scopes.Should().Contain("email");
        scopes.Should().Contain("profile");
    }
}