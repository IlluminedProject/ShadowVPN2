using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Client;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Identities;
using Raven.Client.Exceptions;
using ShadowVPN2.Data;
using ShadowVPN2.Entities;
using ShadowVPN2.Entities.Auth;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Client.OpenIddictClientModels;
using ProtocolException = OpenIddict.Abstractions.OpenIddictExceptions.ProtocolException;

namespace ShadowVPN2.Infrastructure.Authentication;

public sealed class DeviceAuthorizationService(
    IDocumentStore documentStore,
    GlobalConfigurationService configurationService,
    OpenIddictClientService openIddictClient,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<DeviceAuthorizationService> logger,
    DeviceAuthorizationNotificationService notifications) {
    public const string CorrelationCookie = "shadowvpn-device-correlation";

    private readonly IDataProtector _deviceCodeProtector =
        dataProtectionProvider.CreateProtector("ShadowVPN2.DeviceCode");

    public async Task<(DeviceAuthorizationStartResponse Response, string CorrelationSecret)> StartAsync(
        CancellationToken cancellationToken) {
        var provider = GetProvider(await configurationService.GetAsync(cancellationToken));
        var result = await openIddictClient.ChallengeUsingDeviceAsync(new DeviceChallengeRequest {
            ProviderName = provider.SchemeName,
            Scopes = (provider.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } scopes
                ? scopes
                : ["openid", "email", "profile"]).ToList(),
            CancellationToken = cancellationToken
        });
        logger.LogInformation(
            "Device authorization started for {ProviderScheme} with expiration {ExpiresIn} and interval {Interval}",
            provider.SchemeName, result.ExpiresIn, result.Interval);

        var transactionId = Guid.NewGuid().ToString("N");
        var correlationSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var transaction = new DeviceAuthorizationTransaction {
            Id = $"DeviceAuthorizationTransactions/{transactionId}",
            ProviderScheme = provider.SchemeName,
            ProtectedDeviceCode = _deviceCodeProtector.Protect(result.DeviceCode),
            CorrelationHash = Hash(correlationSecret),
            UserCode = result.UserCode,
            VerificationUri = result.VerificationUri.AbsoluteUri,
            VerificationUriComplete = result.VerificationUriComplete?.AbsoluteUri,
            ExpiresAtUtc = DateTime.UtcNow.Add(result.ExpiresIn),
            PollInterval = Math.Max(1, (int)result.Interval.TotalSeconds)
        };

        using var session = documentStore.OpenAsyncSession();
        await session.StoreAsync(transaction, transaction.Id, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);

        return (new DeviceAuthorizationStartResponse {
            TransactionId = transactionId,
            VerificationUri = transaction.VerificationUri,
            VerificationUriComplete = transaction.VerificationUriComplete,
            UserCode = transaction.UserCode,
            ExpiresIn = (int)result.ExpiresIn.TotalSeconds,
            Interval = transaction.PollInterval
        }, correlationSecret);
    }

    public async Task<DeviceAuthorizationPollResponse> PollAsync(string transactionId, string correlationSecret,
        CancellationToken cancellationToken) {
        var transaction = await LoadAsync(transactionId, cancellationToken);
        ValidateCorrelation(transaction, correlationSecret);

        if ((transaction.Status == DeviceAuthorizationStatus.Pending ||
             transaction.Status == DeviceAuthorizationStatus.Processing) &&
            transaction.ExpiresAtUtc <= DateTime.UtcNow) {
            transaction.Status = DeviceAuthorizationStatus.Expired;
            transaction.Error = "The device authorization code expired.";
            await SaveAsync(transaction, cancellationToken);
            notifications.Publish(transactionId, transaction.Status);
        }

        if (transaction.Status == DeviceAuthorizationStatus.Pending)
            await StartProcessingAsync(transaction, correlationSecret, cancellationToken);

        return new DeviceAuthorizationPollResponse {
            Status = transaction.Status.ToString().ToLowerInvariant(),
            RetryAfter = transaction.PollInterval,
            Error = transaction.Error
        };
    }

    public async Task<DeviceAuthorizationStartResponse> GetStartResponseAsync(string transactionId,
        string correlationSecret, CancellationToken cancellationToken) {
        var transaction = await LoadAsync(transactionId, cancellationToken);
        ValidateCorrelation(transaction, correlationSecret);

        return new DeviceAuthorizationStartResponse {
            TransactionId = transactionId,
            VerificationUri = transaction.VerificationUri,
            VerificationUriComplete = transaction.VerificationUriComplete,
            UserCode = transaction.UserCode,
            ExpiresIn = Math.Max(0, (int)(transaction.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds),
            Interval = transaction.PollInterval
        };
    }

    public async Task BeginProcessingAsync(string transactionId, string correlationSecret,
        CancellationToken cancellationToken) {
        var transaction = await LoadAsync(transactionId, cancellationToken);
        ValidateCorrelation(transaction, correlationSecret);
        if (transaction.Status == DeviceAuthorizationStatus.Pending)
            await StartProcessingAsync(transaction, correlationSecret, cancellationToken);
    }

    public async Task<DeviceAuthorizationStatus> GetStatusAsync(string transactionId, string correlationSecret,
        CancellationToken cancellationToken) {
        var transaction = await LoadAsync(transactionId, cancellationToken);
        ValidateCorrelation(transaction, correlationSecret);
        return transaction.Status;
    }

    public async Task<DeviceAuthorizationCompleteResponse> CompleteAsync(string transactionId, string correlationSecret,
        CancellationToken cancellationToken) {
        var transaction = await LoadAsync(transactionId, cancellationToken);
        ValidateCorrelation(transaction, correlationSecret);

        if ((transaction.Status == DeviceAuthorizationStatus.Pending ||
             transaction.Status == DeviceAuthorizationStatus.Processing ||
             transaction.Status == DeviceAuthorizationStatus.Completed) &&
            transaction.ExpiresAtUtc <= DateTime.UtcNow) {
            await MarkExpiredAsync(transactionId, cancellationToken);
            return new DeviceAuthorizationCompleteResponse {
                Status = DeviceAuthorizationStatus.Expired.ToString().ToLowerInvariant(),
                Error = "The device authorization code expired."
            };
        }

        if (transaction.Status != DeviceAuthorizationStatus.Completed) {
            return new DeviceAuthorizationCompleteResponse {
                Status = transaction.Status.ToString().ToLowerInvariant(),
                Error = transaction.Error
            };
        }

        transaction = await ClaimCompletionAsync(transactionId, correlationSecret, cancellationToken);
        if (transaction is null) {
            var current = await LoadAsync(transactionId, cancellationToken);
            if (current.Status == DeviceAuthorizationStatus.Completed &&
                current.ExpiresAtUtc <= DateTime.UtcNow) {
                await MarkExpiredAsync(transactionId, cancellationToken);
                return new DeviceAuthorizationCompleteResponse {
                    Status = DeviceAuthorizationStatus.Expired.ToString().ToLowerInvariant(),
                    Error = "The device authorization code expired."
                };
            }

            return new DeviceAuthorizationCompleteResponse {
                Status = DeviceAuthorizationStatus.Consumed.ToString().ToLowerInvariant(),
                Error = "The device authorization transaction has already been completed."
            };
        }

        if (string.IsNullOrWhiteSpace(transaction.ProviderKey) || string.IsNullOrWhiteSpace(transaction.Email)) {
            return await FailCompletionAsync(transaction, "The provider did not return the required identity claims.",
                cancellationToken);
        }

        try {
            var user = await userManager.FindByLoginAsync(transaction.ProviderScheme, transaction.ProviderKey!);
            if (user is null)
                user = await userManager.FindByEmailAsync(transaction.Email!);

            var isFirstAdmin = false;
            if (user is null) {
                var config = await configurationService.GetAsync(cancellationToken);
                isFirstAdmin = (await userManager.GetUsersInRoleAsync(AppRoles.Administrator)).Count == 0;
                if (!isFirstAdmin && !config.SelfRegistrationEnabled)
                    return await FailCompletionAsync(transaction, "Self-registration is disabled.", cancellationToken);

                var userNumber = (int)await documentStore.Maintenance.SendAsync(
                    new NextIdentityForOperation("UserNumbers"),
                    cancellationToken);
                user = new ApplicationUser {
                    UserName = transaction.Email!,
                    Email = transaction.Email,
                    UserNumber = userNumber,
                    EmailConfirmed = true
                };
                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded) {
                    return await FailCompletionAsync(transaction,
                        string.Join(", ", createResult.Errors.Select(x => x.Description)), cancellationToken);
                }

                var roleResult = await userManager.AddToRoleAsync(user,
                    isFirstAdmin ? AppRoles.Administrator : AppRoles.User);
                if (!roleResult.Succeeded) {
                    return await FailCompletionAsync(transaction,
                        $"Failed to assign role: {string.Join(", ", roleResult.Errors.Select(x => x.Description))}",
                        cancellationToken);
                }
            }
            else if (user.UserNumber == 0) {
                user.UserNumber = (int)await documentStore.Maintenance.SendAsync(
                    new NextIdentityForOperation("UserNumbers"),
                    cancellationToken);
                var updateResult = await userManager.UpdateAsync(user);
                if (!updateResult.Succeeded) {
                    return await FailCompletionAsync(transaction,
                        string.Join(", ", updateResult.Errors.Select(x => x.Description)), cancellationToken);
                }
            }

            var logins = await userManager.GetLoginsAsync(user);
            if (logins.All(x => x.LoginProvider != transaction.ProviderScheme ||
                                x.ProviderKey != transaction.ProviderKey)) {
                var loginResult = await userManager.AddLoginAsync(user, new UserLoginInfo(
                    transaction.ProviderScheme, transaction.ProviderKey!, transaction.ProviderScheme));
                if (!loginResult.Succeeded) {
                    return await FailCompletionAsync(transaction,
                        string.Join(", ", loginResult.Errors.Select(x => x.Description)), cancellationToken);
                }
            }

            await signInManager.SignInAsync(user, false, transaction.ProviderScheme);
            return new DeviceAuthorizationCompleteResponse { Status = "completed" };
        }
        catch (Exception exception) {
            return await FailCompletionAsync(transaction, exception.Message, cancellationToken);
        }
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken) {
        using var session = documentStore.OpenAsyncSession();
        session.Advanced.UseOptimisticConcurrency = true;
        var transactions = await session.Advanced.AsyncDocumentQuery<DeviceAuthorizationTransaction>()
            .WhereLessThanOrEqual(nameof(DeviceAuthorizationTransaction.ExpiresAtUtc), DateTime.UtcNow)
            .WhereIn(nameof(DeviceAuthorizationTransaction.Status), [
                DeviceAuthorizationStatus.Pending,
                DeviceAuthorizationStatus.Processing,
                DeviceAuthorizationStatus.Completed
            ])
            .ToListAsync(cancellationToken);

        foreach (var transaction in transactions) {
            if (transaction.Status == DeviceAuthorizationStatus.Processing) {
                transaction.Status = DeviceAuthorizationStatus.Expired;
                transaction.Error = "The device authorization code expired.";
            }
            else {
                session.Delete(transaction);
            }
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task StartProcessingAsync(DeviceAuthorizationTransaction transaction, string correlationSecret,
        CancellationToken cancellationToken) {
        using var session = documentStore.OpenAsyncSession();
        session.Advanced.UseOptimisticConcurrency = true;
        var stored = await session.LoadAsync<DeviceAuthorizationTransaction>(transaction.Id, cancellationToken);
        if (stored is null || stored.Status != DeviceAuthorizationStatus.Pending) return;
        stored.Status = DeviceAuthorizationStatus.Processing;
        await session.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Device authorization processing started for {TransactionId}", transaction.Id);

        _ = Task.Run(async () => {
            try {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<DeviceAuthorizationService>();
                await service.ProcessAsync(
                    transaction.Id["DeviceAuthorizationTransactions/".Length..], correlationSecret);
            }
            catch (Exception exception) {
                logger.LogError(exception, "Device authorization processing crashed for {TransactionId}",
                    transaction.Id);
            }
        });
        transaction.Status = DeviceAuthorizationStatus.Processing;
    }

    private async Task ProcessAsync(string transactionId, string correlationSecret) {
        try {
            var transaction = await LoadAsync(transactionId, CancellationToken.None);
            ValidateCorrelation(transaction, correlationSecret);
            if (transaction.ExpiresAtUtc <= DateTime.UtcNow) {
                await FailProcessingAsync(transactionId, "The device authorization code expired.",
                    DeviceAuthorizationStatus.Expired);
                return;
            }

            var result = await openIddictClient.AuthenticateWithDeviceAsync(new DeviceAuthenticationRequest {
                ProviderName = transaction.ProviderScheme,
                DeviceCode = _deviceCodeProtector.Unprotect(transaction.ProtectedDeviceCode),
                Timeout = transaction.ExpiresAtUtc - DateTime.UtcNow,
                Interval = TimeSpan.FromSeconds(transaction.PollInterval),
                CancellationToken = CancellationToken.None
            });
            logger.LogInformation("Device authorization provider confirmed {TransactionId}", transactionId);
            var principal = result.Principal;
            var providerKey = principal.FindFirstValue(Claims.Subject);
            var email = principal.FindFirstValue(Claims.Email) ?? principal.FindFirstValue(ClaimTypes.Email);
            var displayName = principal.Identity?.Name ?? email;
            if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email)) {
                await FailProcessingAsync(transactionId, "The provider did not return the required identity claims.");
                return;
            }

            await CompleteProcessingAsync(transactionId, providerKey, email, displayName);
        }
        catch (ProtocolException exception) {
            logger.LogWarning(exception, "Device authorization provider rejected {TransactionId} with {Error}",
                transactionId, exception.Error);
            await FailProcessingAsync(transactionId,
                exception.ErrorDescription ?? exception.Error ?? "Device authorization failed.",
                exception.Error == Errors.AccessDenied
                    ? DeviceAuthorizationStatus.Denied
                    : DeviceAuthorizationStatus.Failed);
        }
        catch (Exception exception) {
            logger.LogError(exception, "Device authorization processing failed for {TransactionId}", transactionId);
            await FailProcessingAsync(transactionId, exception.Message);
        }
    }

    private async Task<DeviceAuthorizationTransaction?> ClaimCompletionAsync(string transactionId,
        string correlationSecret, CancellationToken cancellationToken) {
        using var session = documentStore.OpenAsyncSession();
        session.Advanced.UseOptimisticConcurrency = true;
        var transaction = await session.LoadAsync<DeviceAuthorizationTransaction>(
            $"DeviceAuthorizationTransactions/{transactionId}", cancellationToken);
        if (transaction is null)
            return null;
        ValidateCorrelation(transaction, correlationSecret);
        if (transaction.Status != DeviceAuthorizationStatus.Completed ||
            transaction.ExpiresAtUtc <= DateTime.UtcNow)
            return null;

        transaction.Status = DeviceAuthorizationStatus.Consumed;
        try {
            await session.SaveChangesAsync(cancellationToken);
            return transaction;
        }
        catch (ConcurrencyException) {
            return null;
        }
    }

    private async Task MarkExpiredAsync(string transactionId, CancellationToken cancellationToken) {
        using var session = documentStore.OpenAsyncSession();
        session.Advanced.UseOptimisticConcurrency = true;
        var transaction = await session.LoadAsync<DeviceAuthorizationTransaction>(
            $"DeviceAuthorizationTransactions/{transactionId}", cancellationToken);
        if (transaction is null || (transaction.Status != DeviceAuthorizationStatus.Pending &&
                                    transaction.Status != DeviceAuthorizationStatus.Processing &&
                                    transaction.Status != DeviceAuthorizationStatus.Completed))
            return;
        transaction.Status = DeviceAuthorizationStatus.Expired;
        transaction.Error = "The device authorization code expired.";
        await session.SaveChangesAsync(cancellationToken);
        notifications.Publish(transactionId, transaction.Status);
    }

    private async Task<DeviceAuthorizationCompleteResponse> FailCompletionAsync(
        DeviceAuthorizationTransaction transaction, string error, CancellationToken cancellationToken) {
        transaction.Status = DeviceAuthorizationStatus.Failed;
        transaction.Error = error;
        await SaveAsync(transaction, cancellationToken);
        return new DeviceAuthorizationCompleteResponse {
            Status = DeviceAuthorizationStatus.Failed.ToString().ToLowerInvariant(),
            Error = error
        };
    }

    private async Task CompleteProcessingAsync(string transactionId, string providerKey, string email,
        string? displayName) {
        using var session = documentStore.OpenAsyncSession();
        session.Advanced.UseOptimisticConcurrency = true;
        var transaction = await session.LoadAsync<DeviceAuthorizationTransaction>(
            $"DeviceAuthorizationTransactions/{transactionId}");
        if (transaction is null || transaction.Status != DeviceAuthorizationStatus.Processing)
            return;
        if (transaction.ExpiresAtUtc <= DateTime.UtcNow) {
            transaction.Status = DeviceAuthorizationStatus.Expired;
            transaction.Error = "The device authorization code expired.";
        }
        else {
            transaction.ProviderKey = providerKey;
            transaction.Email = email;
            transaction.DisplayName = displayName;
            transaction.Status = DeviceAuthorizationStatus.Completed;
            transaction.Error = null;
        }

        await session.SaveChangesAsync();
        notifications.Publish(transactionId, transaction.Status);
    }

    private async Task FailProcessingAsync(string transactionId, string error,
        DeviceAuthorizationStatus status = DeviceAuthorizationStatus.Failed) {
        try {
            using var session = documentStore.OpenAsyncSession();
            session.Advanced.UseOptimisticConcurrency = true;
            var transaction = await session.LoadAsync<DeviceAuthorizationTransaction>(
                $"DeviceAuthorizationTransactions/{transactionId}");
            if (transaction is null || transaction.Status != DeviceAuthorizationStatus.Processing)
                return;
            transaction.Status = status;
            transaction.Error = error;
            await session.SaveChangesAsync();
            notifications.Publish(transactionId, transaction.Status);
        }
        catch (InvalidOperationException) {
            // The transaction may have expired and been removed while the provider request was running.
        }
    }

    private static OidcAuthProvider GetProvider(EntityGlobalConfiguration config) {
        return config.Providers.OfType<OidcAuthProvider>().FirstOrDefault(x => x.IsEnabled && x.DeviceFlowEnabled)
               ?? throw new InvalidOperationException("Device authorization is not enabled.");
    }

    private async Task<DeviceAuthorizationTransaction> LoadAsync(string transactionId,
        CancellationToken cancellationToken) {
        using var session = documentStore.OpenAsyncSession();
        return await session.LoadAsync<DeviceAuthorizationTransaction>(
                   $"DeviceAuthorizationTransactions/{transactionId}", cancellationToken)
               ?? throw new InvalidOperationException("Device authorization transaction was not found.");
    }

    private async Task SaveAsync(DeviceAuthorizationTransaction transaction, CancellationToken cancellationToken) {
        using var session = documentStore.OpenAsyncSession();
        await session.StoreAsync(transaction, transaction.Id, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateCorrelation(DeviceAuthorizationTransaction transaction, string secret) {
        if (string.IsNullOrWhiteSpace(secret) || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(transaction.CorrelationHash), SHA256.HashData(Encoding.UTF8.GetBytes(secret))))
            throw new UnauthorizedAccessException("Invalid device authorization transaction.");
    }

    private static string Hash(string value) {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}