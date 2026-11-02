using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Application.Compatibility;
using ECommerceStore.Pact.Domain.Compatibility;
using ECommerceStore.Pact.Infrastructure.Registry;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Compatibility;

internal sealed class MongoCompatibilityRepository(RegistryCollections collections, RegistryIndexes indexes) : IVerificationProfileRepository, ICompatibilityDecisionRepository
{
    public Task<RegistrationResult<VerificationProfile>> RegisterAsync(VerificationProfile candidate, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        await indexes.EnsureCreatedAsync(cancellationToken);
        var document = CompatibilityMapping.ToDocument(candidate);
        var stored = await InsertOrRead(collections.VerificationProfiles, Builders<VerificationProfileDocument>.Filter.Eq(value => value.Key, candidate.Key), document, cancellationToken);
        return new RegistrationResult<VerificationProfile>(CompatibilityMapping.ToDomain(stored), stored.Id == document.Id);
    });
    public Task<VerificationProfile?> GetAsync(Guid id, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        var value = await collections.VerificationProfiles.Find(value => value.Id == id.ToString("D")).FirstOrDefaultAsync(cancellationToken);
        return value is null ? null : CompatibilityMapping.ToDomain(value);
    });
    public Task<RegistrationResult<CompatibilityDecision>> AppendAsync(CompatibilityDecision candidate, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        await indexes.EnsureCreatedAsync(cancellationToken);
        var document = CompatibilityMapping.ToDocument(candidate);
        var stored = await InsertOrRead(collections.CompatibilityDecisions, Builders<CompatibilityDecisionDocument>.Filter.Eq(value => value.Key, candidate.Key), document, cancellationToken);
        return new RegistrationResult<CompatibilityDecision>(CompatibilityMapping.ToDomain(stored), stored.Id == document.Id);
    });
    Task<CompatibilityDecision?> ICompatibilityDecisionRepository.GetAsync(Guid id, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        var value = await collections.CompatibilityDecisions.Find(value => value.Id == id.ToString("D")).FirstOrDefaultAsync(cancellationToken);
        return value is null ? null : CompatibilityMapping.ToDomain(value);
    });
    private static async Task<T> InsertOrRead<T>(IMongoCollection<T> collection, FilterDefinition<T> filter, T candidate, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await collection.FindOneAndUpdateAsync(filter, new BsonDocument("$setOnInsert", candidate.ToBsonDocument()),
                new FindOneAndUpdateOptions<T> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken)
                ?? throw new InvalidOperationException("Inserted compatibility record could not be read.");
        }
        catch (Exception exception) when (MongoRegistryOperation.IsDuplicateKey(exception))
        { return await collection.Find(filter).FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Concurrent compatibility record could not be read."); }
    }
}
