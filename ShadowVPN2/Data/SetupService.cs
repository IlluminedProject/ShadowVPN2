using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace ShadowVPN2.Data;

public class AdminSetupRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class NodeSetupRequest
{
    public string NodeAddress { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
}

public class SetupService(
    IServiceProvider serviceProvider,
    IHttpClientFactory httpClientFactory,
    Infrastructure.Configurations.LocalConfiguration localConfiguration,
    IDocumentStore documentStore)
{
    public async Task<bool> NeedsSetupAsync()
    {
        using var session = documentStore.OpenAsyncSession();

        var hasAnyUser = await session.Query<ApplicationUser>().AnyAsync();
        return !hasAnyUser;
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

        var node = new Entities.EntityClusterNode
        {
            Id = "EntityClusterNodes|",
            NodeId = localConfiguration.NodeId,
            Name = request.NodeName,
            Address = request.NodeAddress
        };

        await session.StoreAsync(node);
        await session.SaveChangesAsync();
    }

    public async Task CreateAdminAsync(AdminSetupRequest request)
    {
        if (!await NeedsSetupAsync())
        {
            throw new InvalidOperationException("Setup is already completed.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Email and Password are required.");
        }

        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                result.Errors.Select(e => e.Description)));
        }
    }
}