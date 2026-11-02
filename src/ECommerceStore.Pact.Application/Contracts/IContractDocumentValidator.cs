using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;

namespace ECommerceStore.Pact.Application.Contracts;

public interface IContractDocumentValidator
{
    Task<string> ValidateAsync(ContractKind kind, byte[] content, IntegrationIdentity? integration, CancellationToken cancellationToken);
}
