using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;
using ShadowVPN2.Infrastructure.Authentication;

namespace ShadowVPN2.Data;

public class SetupService(
    IServiceProvider serviceProvider,
    IHttpClientFactory httpClientFactory,
    Infrastructure.Configurations.LocalConfiguration localConfiguration,
    IDocumentStore documentStore,
    DynamicAuthenticationManager authManager,
    ILogger<SetupService> logger)
{
    public Task<bool> NeedsSetupAsync()
    {
        return Task.FromResult(File.Exists(Infrastructure.Configurations.LocalConfiguration.RootCaPfxPath.Value));
    }

    public async Task<string?> GetPublicIpAsync()
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var publicIp = await client.GetStringAsync("https://api.ipify.org");
            return publicIp.Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task ConfigureNodeAsync(NodeSetupRequest request)
    {
        if (!await NeedsSetupAsync())
        {
            throw new InvalidOperationException("Setup is already completed.");
        }

        if (string.IsNullOrWhiteSpace(request.NodeAddress) || string.IsNullOrWhiteSpace(request.NodeName))
        {
            throw new ArgumentException("Node Address and Node Name are required.");
        }

        using var session = documentStore.OpenAsyncSession(new Raven.Client.Documents.Session.SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });

        var node = new EntityClusterNode
        {
            Id = "EntityClusterNodes|",
            NodeId = localConfiguration.NodeId,
            Name = request.NodeName,
            Address = request.NodeAddress
        };

        await session.StoreAsync(node);
        await session.SaveChangesAsync();
    }

    public async Task<bool> TestOidcConnectionAsync(string authority)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(authority)) return false;

            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var discoveryUrl = authority.TrimEnd('/') + "/.well-known/openid-configuration";
            logger.LogInformation("Testing OIDC discovery endpoint at {DiscoveryUrl}", discoveryUrl);
            var response = await client.GetAsync(discoveryUrl);

            logger.LogInformation("OIDC discovery endpoint returned {StatusCode}", response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to connect to OIDC authority {Authority}", authority);
            return false;
        }
    }

    public async Task ConfigureLocalAuthAsync(LocalAuthSetupRequest request)
    {
        if (!await NeedsSetupAsync())
        {
            throw new InvalidOperationException("Setup is already completed.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ArgumentException("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Password is required for local login.");
        }

        using var session = documentStore.OpenAsyncSession(new Raven.Client.Documents.Session.SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });

        var globalConfig = await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration");
        if (globalConfig == null)
        {
            globalConfig = new EntityGlobalConfiguration();
            await session.StoreAsync(globalConfig);
        }

        if (!globalConfig.Providers.OfType<LocalAuthProvider>().Any())
        {
            globalConfig.Providers.Add(new LocalAuthProvider());
        }

        await session.SaveChangesAsync();

        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                result.Errors.Select(e => e.Description)));
        }

        logger.LogInformation("Local auth configured, admin user {Email} created", request.Email);
    }

    public async Task ConfigureOidcAsync(OidcAuthSetupRequest request)
    {
        if (!await NeedsSetupAsync())
        {
            throw new InvalidOperationException("Setup is already completed.");
        }

        if (string.IsNullOrWhiteSpace(request.Authority) ||
            string.IsNullOrWhiteSpace(request.ClientId) ||
            string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            throw new ArgumentException("Authority, Client ID, and Client Secret are required for OIDC.");
        }

        using var session = documentStore.OpenAsyncSession(new Raven.Client.Documents.Session.SessionOptions
            { TransactionMode = TransactionMode.ClusterWide });

        var globalConfig = await session.LoadAsync<EntityGlobalConfiguration>("GlobalConfiguration");
        if (globalConfig == null)
        {
            globalConfig = new EntityGlobalConfiguration();
            await session.StoreAsync(globalConfig);
        }

        var existingOidc = globalConfig.Providers.OfType<OidcAuthProvider>()
            .FirstOrDefault(p => p.SchemeName == request.SchemeName);

        if (existingOidc != null)
        {
            existingOidc.DisplayName = request.DisplayName;
            existingOidc.Authority = request.Authority;
            existingOidc.ClientId = request.ClientId;
            existingOidc.ClientSecret = request.ClientSecret;
            existingOidc.IsEnabled = true;
        }
        else
        {
            existingOidc = new OidcAuthProvider
            {
                SchemeName = request.SchemeName,
                DisplayName = request.DisplayName,
                Authority = request.Authority,
                ClientId = request.ClientId,
                ClientSecret = request.ClientSecret,
                IsEnabled = true
            };
            globalConfig.Providers.Add(existingOidc);
        }

        await session.SaveChangesAsync();

        logger.LogInformation("OIDC provider {SchemeName} configured with authority {Authority}", request.SchemeName, request.Authority);
        await authManager.AddOrUpdateOidcProviderAsync(existingOidc);
    }

    public async Task<byte[]> GetRootCaBytesAsync()
    {
        if (!await NeedsSetupAsync())
        {
            throw new InvalidOperationException("Setup is already completed.");
        }

        return await File.ReadAllBytesAsync(Infrastructure.Configurations.LocalConfiguration.RootCaPfxPath.Value);
    }

    public async Task FinishSetupAsync()
    {
        if (!await NeedsSetupAsync())
        {
            throw new InvalidOperationException("Setup is already completed.");
        }

        File.Delete(Infrastructure.Configurations.LocalConfiguration.RootCaPfxPath.Value);
        await Task.CompletedTask;
    }
}