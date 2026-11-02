using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Application;

public sealed class IntegrationService(IIntegrationRepository repository, TimeProvider timeProvider)
{
    public Task<RegistrationResult<Integration>> RegisterAsync(string? consumerApplicationId, string? providerApplicationId,
        CancellationToken cancellationToken) =>
        repository.RegisterAsync(Integration.Register(new IntegrationIdentity(consumerApplicationId, providerApplicationId),
            timeProvider.GetUtcNow()), cancellationToken);

    public Task<Integration?> GetAsync(string? consumerApplicationId, string? providerApplicationId,
        CancellationToken cancellationToken) =>
        repository.GetAsync(new IntegrationIdentity(consumerApplicationId, providerApplicationId), cancellationToken);
}
