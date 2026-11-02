using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Application;

public interface IIntegrationRepository
{
    Task<RegistrationResult<Integration>> RegisterAsync(Integration candidate, CancellationToken cancellationToken);
    Task<Integration?> GetAsync(IntegrationIdentity identity, CancellationToken cancellationToken);
}
