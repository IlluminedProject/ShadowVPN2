using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using ShadowVPN2.Data.Protocols;
using ShadowVPN2.Entities;
using ShadowVPN2.Infrastructure.Configurations;

namespace ShadowVPN2.Data.Certificates;

public sealed class AcmeCertificateService(
    IDocumentStore documentStore,
    IOptions<LocalConfiguration> localConfiguration,
    GlobalConfigurationService globalConfigurationService,
    ProtocolDomainService protocolDomainService,
    ManagedCertificateService managedCertificateService,
    Http01ChallengeServer challengeServer,
    ILogger<AcmeCertificateService> logger) : BackgroundService {
    private readonly SemaphoreSlim _renewalLock = new(1, 1);

    public async Task RenewNowAsync(CancellationToken cancellationToken = default) {
        var settings = (await globalConfigurationService.GetAsync(cancellationToken)).AcmeSettings;
        Validate(settings);

        await _renewalLock.WaitAsync(cancellationToken);
        try {
            var nodeId = localConfiguration.Value.NodeId;
            var desired = await protocolDomainService.GetCertificateDomainsAsync(nodeId, cancellationToken);
            if (desired.Domains.Count == 0) {
                logger.LogInformation("No validated domains available for ACME certificate");
                return;
            }

            var current = await managedCertificateService.GetEntityAsync(nodeId, cancellationToken);
            var domainsChanged = current == null || !current.Domains.Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(desired.Domains.Order(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            var expiresSoon = current == null || current.NotAfter <=
                DateTimeOffset.UtcNow.AddDays(settings.RenewalThresholdDays);
            if (!domainsChanged && !expiresSoon) return;

            await IssueAsync(nodeId, desired.Domains, settings, cancellationToken);
        }
        finally {
            _renewalLock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        while (!stoppingToken.IsCancellationRequested) {
            try {
                var settings = (await globalConfigurationService.GetAsync(stoppingToken)).AcmeSettings;
                if (settings.Enabled)
                    await RenewNowAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                logger.LogError(ex, "ACME certificate renewal failed");
                try {
                    await managedCertificateService.RecordErrorAsync(localConfiguration.Value.NodeId, ex.Message,
                        stoppingToken);
                }
                catch (Exception recordException) {
                    logger.LogError(recordException, "Failed to record ACME renewal error");
                }
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    private async Task IssueAsync(Guid nodeId, IReadOnlyList<string> domains, AcmeSettings settings,
        CancellationToken cancellationToken) {
        await challengeServer.StartAsync(settings.HttpPort, cancellationToken);
        try {
            var acme = await CreateContextAsync(settings, cancellationToken);
            var order = await acme.NewOrder(domains.ToList());
            var authorizations = (await order.Authorizations()).ToList();
            var challenges = new List<IChallengeContext>(authorizations.Count);
            foreach (var authorization in authorizations) {
                var challenge = await authorization.Http();
                challengeServer.Set(challenge.Token, challenge.KeyAuthz);
                challenges.Add(challenge);
            }

            foreach (var challenge in challenges) await challenge.Validate();
            foreach (var authorization in authorizations)
                await WaitForAuthorizationAsync(authorization, cancellationToken);

            var certificateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
            var certificate = await order.Generate(new CsrInfo(), certificateKey, retryCount: 10);
            await managedCertificateService.SaveAsync(nodeId, domains, certificate.ToPem(), certificateKey.ToPem(),
                cancellationToken);
            logger.LogInformation("ACME certificate issued for {Domains}", string.Join(", ", domains));
        }
        finally {
            await challengeServer.StopAsync(CancellationToken.None);
        }
    }

    private async Task<AcmeContext> CreateContextAsync(AcmeSettings settings, CancellationToken cancellationToken) {
        var staging = settings.UseStaging;
        var environment = staging ? "staging" : "production";
        var id = $"AcmeAccounts/{localConfiguration.Value.NodeId:D}/{environment}";
        using var session = documentStore.OpenAsyncSession();
        var entity = await session.LoadAsync<EntityAcmeAccount>(id, cancellationToken);
        var directory = staging ? WellKnownServers.LetsEncryptStagingV2 : WellKnownServers.LetsEncryptV2;
        if (entity != null) {
            var existing = new AcmeContext(directory, KeyFactory.FromPem(entity.AccountKeyPem));
            await existing.Account();
            return existing;
        }

        var created = new AcmeContext(directory);
        await created.NewAccount(settings.Email, true);
        entity = new EntityAcmeAccount {
            Id = id,
            AccountKeyPem = created.AccountKey.ToPem(),
            Email = settings.Email,
            IsStaging = staging
        };
        await session.StoreAsync(entity, id, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
        return created;
    }

    private static void Validate(AcmeSettings settings) {
        if (!settings.Enabled) throw new InvalidOperationException("ACME is disabled");
        if (string.IsNullOrWhiteSpace(settings.Email)) throw new InvalidOperationException("ACME email is required");
        if (settings.HttpPort is <= 0 or > 65535) throw new InvalidOperationException("ACME HTTP port is invalid");
        if (settings.RenewalThresholdDays is <= 0 or > 89)
            throw new InvalidOperationException("ACME renewal threshold must be between 1 and 89 days");
    }

    private static async Task WaitForAuthorizationAsync(IAuthorizationContext authorization,
        CancellationToken cancellationToken) {
        for (var attempt = 0; attempt < 60; attempt++) {
            var resource = await authorization.Resource();
            if (resource.Status == AuthorizationStatus.Valid) return;
            if (resource.Status == AuthorizationStatus.Invalid)
                throw new InvalidOperationException($"ACME authorization failed for {resource.Identifier.Value}");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException("ACME authorization timed out");
    }
}