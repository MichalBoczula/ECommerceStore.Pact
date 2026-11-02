using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Compatibility;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Application.Compatibility;

public interface IVerificationProfileRepository
{
    Task<RegistrationResult<VerificationProfile>> RegisterAsync(VerificationProfile profile, CancellationToken cancellationToken);
    Task<VerificationProfile?> GetAsync(Guid id, CancellationToken cancellationToken);
}
public interface ICompatibilityDecisionRepository
{
    Task<RegistrationResult<CompatibilityDecision>> AppendAsync(CompatibilityDecision decision, CancellationToken cancellationToken);
    Task<CompatibilityDecision?> GetAsync(Guid id, CancellationToken cancellationToken);
}
public interface ICompatibilityEvidenceReader
{
    Task<ContractBinding?> GetConsumerBindingAsync(Guid consumerVersionId, IntegrationIdentity integration, CancellationToken cancellationToken);
    Task<IReadOnlyList<VerificationRun>> FindExecutionsAsync(IReadOnlyList<string> executionKeys, CancellationToken cancellationToken);
}
public sealed class VerificationProfileConflictException() : Exception("This immutable verification profile already has different required checks.");
public sealed record RegisterVerificationProfileCommand(string ConsumerApplicationId, string ProviderApplicationId, string Name, string Version,
    IReadOnlyList<RequiredVerificationCheck> Checks);
public sealed record CompatibilityRowCommand(string ConsumerCommitSha, string ProviderCommitSha, string ContractSha256, string CiRunId,
    int CiAttempt, string ProviderSourceRef);
public sealed record EvaluateCompatibilityCommand(string SourceApplicationId, Guid ProfileId, IReadOnlyList<CompatibilityRowCommand> Rows);
