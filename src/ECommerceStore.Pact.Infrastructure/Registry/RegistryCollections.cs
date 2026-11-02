using ECommerceStore.Pact.Infrastructure.Contracts;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal sealed class RegistryCollections(IMongoDatabase database)
{
    internal IMongoCollection<ApplicationVersionDocument> Versions { get; } =
        database.GetCollection<ApplicationVersionDocument>("application_versions");
    internal IMongoCollection<IntegrationDocument> Integrations { get; } =
        database.GetCollection<IntegrationDocument>("integrations");
    internal IMongoCollection<ContractArtifactDocument> ContractArtifacts { get; } = database.GetCollection<ContractArtifactDocument>("contract_artifacts");
    internal IMongoCollection<ContractBindingDocument> ContractBindings { get; } = database.GetCollection<ContractBindingDocument>("contract_versions");
    internal IMongoCollection<ContractPublicationDocument> ContractPublications { get; } = database.GetCollection<ContractPublicationDocument>("contract_publications");
}
