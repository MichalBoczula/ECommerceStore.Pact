using ECommerceStore.Pact.Domain.Contracts;

namespace ECommerceStore.Pact.Application.Contracts;

public sealed record PublishedContract(ContractPublication Publication, ContractBinding Binding, ContractArtifact Artifact);
