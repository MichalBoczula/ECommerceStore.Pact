using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Application;

public interface IApplicationVersionRepository
{
    Task<RegistrationResult<ApplicationVersion>> RegisterAsync(ApplicationVersion candidate, CancellationToken cancellationToken);
    Task<ApplicationVersion?> GetAsync(ApplicationVersionIdentity identity, CancellationToken cancellationToken);
}
