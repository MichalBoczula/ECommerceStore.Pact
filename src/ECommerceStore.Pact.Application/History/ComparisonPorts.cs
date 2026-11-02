using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.History;

namespace ECommerceStore.Pact.Application.History;

public interface IContractSchemaComparer
{
    Task<SchemaAnalysis> CompareAsync(ContractArtifact before, ContractArtifact after, CancellationToken cancellationToken);
}
public interface IContractComparisonRepository
{
    Task<ContractComparison?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<ContractComparison?> GetByKeyAsync(string key, CancellationToken cancellationToken);
    Task<RegistrationResult<ContractComparison>> AppendAsync(ContractComparison value, CancellationToken cancellationToken);
}
public sealed record CompareContractsCommand(string SourceApplicationId, string ConsumerApplicationId, string ProviderApplicationId,
    Guid BeforePublicationId, Guid AfterPublicationId, string BaselineRef);
public sealed class ComparisonUnavailableException(Exception inner) : Exception("Contract comparison is unavailable.", inner);
