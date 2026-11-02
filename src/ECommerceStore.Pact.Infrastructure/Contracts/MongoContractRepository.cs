using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Infrastructure.Registry;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Contracts;

internal sealed class MongoContractRepository(RegistryCollections collections, RegistryIndexes indexes) : IContractRepository
{
    public Task<RegistrationResult<ContractBinding>> BindAsync(ContractArtifact artifact, ContractBinding candidate, CancellationToken cancellationToken) =>
        MongoRegistryOperation.ExecuteAsync(async () =>
        {
            await indexes.EnsureCreatedAsync(cancellationToken);
            var document = ContractMapping.ToDocument(candidate);
            var filter = Builders<ContractBindingDocument>.Filter.Eq(value => value.SourceVersionId, document.SourceVersionId) &
                Builders<ContractBindingDocument>.Filter.Eq(value => value.Kind, document.Kind) &
                Builders<ContractBindingDocument>.Filter.Eq(value => value.IntegrationKey, document.IntegrationKey);
            var existing = await collections.ContractBindings.Find(filter).FirstOrDefaultAsync(cancellationToken);
            if (existing is not null) return new RegistrationResult<ContractBinding>(ContractMapping.ToDomain(existing), false);

            // Write content first so a binding can never reference absent content. A losing
            // conflicting race can leave an unreferenced immutable artifact, never a bad binding.
            var artifactDocument = ContractMapping.ToDocument(artifact);
            var storedArtifact = await InsertOrRead(collections.ContractArtifacts,
                Builders<ContractArtifactDocument>.Filter.Eq(value => value.Id, artifact.Sha256), artifactDocument, cancellationToken);
            var readArtifact = ContractMapping.ToDomain(storedArtifact);
            if (readArtifact.Kind != artifact.Kind || readArtifact.SpecificationVersion != artifact.SpecificationVersion)
                throw new InvalidOperationException("Stored artifact metadata conflicts with its validated content.");
            var stored = await InsertOrRead(collections.ContractBindings, filter, document, cancellationToken);
            return new RegistrationResult<ContractBinding>(ContractMapping.ToDomain(stored), stored.Id == document.Id);
        });

    public Task<RegistrationResult<ContractPublication>> PublishAsync(ContractPublication candidate, string repositoryUrl, CancellationToken cancellationToken) =>
        MongoRegistryOperation.ExecuteAsync(async () =>
        {
            await indexes.EnsureCreatedAsync(cancellationToken);
            var document = ContractMapping.ToDocument(candidate, repositoryUrl);
            var filter = Builders<ContractPublicationDocument>.Filter.Eq(value => value.BindingId, document.BindingId) &
                Builders<ContractPublicationDocument>.Filter.Eq(value => value.ProvenanceHash, document.ProvenanceHash);
            var stored = await InsertOrRead(collections.ContractPublications, filter, document, cancellationToken);
            return new RegistrationResult<ContractPublication>(ContractMapping.ToDomain(stored), stored.Id == document.Id);
        });

    public Task<ContractPublication?> GetPublicationAsync(Guid id, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        var value = await collections.ContractPublications.Find(document => document.Id == id.ToString("D")).FirstOrDefaultAsync(cancellationToken);
        return value is null ? null : ContractMapping.ToDomain(value);
    });
    public Task<ContractBinding?> GetBindingAsync(Guid id, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        var value = await collections.ContractBindings.Find(document => document.Id == id.ToString("D")).FirstOrDefaultAsync(cancellationToken);
        return value is null ? null : ContractMapping.ToDomain(value);
    });
    public Task<ContractArtifact?> GetArtifactAsync(string hash, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        var value = await collections.ContractArtifacts.Find(document => document.Id == hash).FirstOrDefaultAsync(cancellationToken);
        return value is null ? null : ContractMapping.ToDomain(value);
    });

    private static async Task<T> InsertOrRead<T>(IMongoCollection<T> collection, FilterDefinition<T> filter, T candidate, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var stored = await collection.FindOneAndUpdateAsync(filter, new BsonDocument("$setOnInsert", candidate.ToBsonDocument()),
                new FindOneAndUpdateOptions<T> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken);
            return stored ?? throw new InvalidOperationException("An inserted contract record could not be read.");
        }
        catch (Exception exception) when (MongoRegistryOperation.IsDuplicateKey(exception))
        {
            return await collection.Find(filter).FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("A concurrent contract record could not be read.");
        }
    }
}
