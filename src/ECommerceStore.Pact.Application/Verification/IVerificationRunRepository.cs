using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Application.Verification;

public interface IVerificationRunRepository
{
    Task<RegistrationResult<VerificationRun>> AppendAsync(VerificationRun run, CancellationToken cancellationToken);
    Task<VerificationRun?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<VerificationRun>> ListAsync(IntegrationIdentity integration, string contractSha256, Guid? after, int take, CancellationToken cancellationToken);
}
