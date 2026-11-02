using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Application.History;
using ECommerceStore.Pact.Domain.History;
using ECommerceStore.Pact.Infrastructure.Registry;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.History;

internal sealed class MongoContractComparisonRepository(RegistryCollections collections, RegistryIndexes indexes) : IContractComparisonRepository
{
    public Task<ContractComparison?> GetAsync(Guid id, CancellationToken cancellationToken) => ReadAsync(Builders<ContractComparisonDocument>.Filter.Eq(value => value.Id, id.ToString("D")), cancellationToken);
    public Task<ContractComparison?> GetByKeyAsync(string key, CancellationToken cancellationToken) => ReadAsync(Builders<ContractComparisonDocument>.Filter.Eq(value => value.Key, key), cancellationToken);
    private Task<ContractComparison?> ReadAsync(FilterDefinition<ContractComparisonDocument> filter, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        var value = await collections.ContractComparisons.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return value is null ? null : ComparisonMapping.ToDomain(value);
    });
    public Task<RegistrationResult<ContractComparison>> AppendAsync(ContractComparison candidate, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        await indexes.EnsureCreatedAsync(cancellationToken);
        var document = ComparisonMapping.ToDocument(candidate);
        var filter = Builders<ContractComparisonDocument>.Filter.Eq(value => value.Key, candidate.Key);
        ContractComparisonDocument stored;
        try
        {
            stored = await collections.ContractComparisons.FindOneAndUpdateAsync(filter, new BsonDocument("$setOnInsert", document.ToBsonDocument()),
                new FindOneAndUpdateOptions<ContractComparisonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken);
        }
        catch (Exception exception) when (MongoRegistryOperation.IsDuplicateKey(exception))
        { stored = await collections.ContractComparisons.Find(filter).FirstOrDefaultAsync(cancellationToken); }
        if (stored is null) throw new InvalidOperationException("Inserted comparison could not be read.");
        return new RegistrationResult<ContractComparison>(ComparisonMapping.ToDomain(stored), stored.Id == document.Id);
    });
}
