using ECommerceStore.Pact.Domain.Contracts;

namespace ECommerceStore.Pact.Application.Contracts;

public interface IContractRepository
{
    Task<RegistrationResult<ContractBinding>> BindAsync(ContractArtifact artifact, ContractBinding binding, CancellationToken cancellationToken);
    Task<RegistrationResult<ContractPublication>> PublishAsync(ContractPublication publication, string repositoryUrl, CancellationToken cancellationToken);
    Task<ContractPublication?> GetPublicationAsync(Guid id, CancellationToken cancellationToken);
    Task<ContractBinding?> GetBindingAsync(Guid id, CancellationToken cancellationToken);
    Task<ContractArtifact?> GetArtifactAsync(string hash, CancellationToken cancellationToken);
}
