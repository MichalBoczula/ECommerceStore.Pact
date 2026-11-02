using ECommerceStore.Pact.Infrastructure.Contracts;
using MongoDB.Driver;
using ECommerceStore.Pact.Infrastructure.History;
using ECommerceStore.Pact.Infrastructure.Verification;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal sealed class RegistryCollections(IMongoDatabase database)
{
    internal IMongoCollection<ContractComparisonDocument> ContractComparisons { get; } = database.GetCollection<ContractComparisonDocument>("contract_comparisons");
    internal IMongoCollection<VerificationRunDocument> VerificationRuns { get; } = database.GetCollection<VerificationRunDocument>("verification_runs");
    internal IMongoCollection<ApplicationVersionDocument> Versions { get; } =
        database.GetCollection<ApplicationVersionDocument>("application_versions");
    internal IMongoCollection<IntegrationDocument> Integrations { get; } =
        database.GetCollection<IntegrationDocument>("integrations");
    internal IMongoCollection<ContractArtifactDocument> ContractArtifacts { get; } = database.GetCollection<ContractArtifactDocument>("contract_artifacts");
    internal IMongoCollection<ContractBindingDocument> ContractBindings { get; } = database.GetCollection<ContractBindingDocument>("contract_versions");
    internal IMongoCollection<ContractPublicationDocument> ContractPublications { get; } = database.GetCollection<ContractPublicationDocument>("contract_publications");
}
