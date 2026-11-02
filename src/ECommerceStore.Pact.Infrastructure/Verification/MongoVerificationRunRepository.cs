using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Application.Verification;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Verification;
using ECommerceStore.Pact.Infrastructure.Registry;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Verification;

internal sealed class MongoVerificationRunRepository(RegistryCollections collections, RegistryIndexes indexes) : IVerificationRunRepository
{
    public Task<RegistrationResult<VerificationRun>> AppendAsync(VerificationRun candidate, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        await indexes.EnsureCreatedAsync(cancellationToken);
        var document = VerificationMapping.ToDocument(candidate);
        var filter = Builders<VerificationRunDocument>.Filter.Eq(value => value.ExecutionKey, candidate.ExecutionKey);
        VerificationRunDocument stored;
        try
        {
            stored = await collections.VerificationRuns.FindOneAndUpdateAsync(filter, new BsonDocument("$setOnInsert", document.ToBsonDocument()),
                new FindOneAndUpdateOptions<VerificationRunDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken);
        }
        catch (Exception exception) when (MongoRegistryOperation.IsDuplicateKey(exception))
        { stored = await collections.VerificationRuns.Find(filter).FirstOrDefaultAsync(cancellationToken); }
        if (stored is null) throw new InvalidOperationException("An inserted verification run could not be read.");
        return new RegistrationResult<VerificationRun>(VerificationMapping.ToDomain(stored), stored.Id == document.Id);
    });

    public Task<VerificationRun?> GetAsync(Guid id, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        var value = await collections.VerificationRuns.Find(run => run.Id == id.ToString("D")).FirstOrDefaultAsync(cancellationToken);
        return value is null ? null : VerificationMapping.ToDomain(value);
    });

    public Task<IReadOnlyList<VerificationRun>> ListAsync(IntegrationIdentity integration, string hash, Guid? after, int take, CancellationToken cancellationToken) =>
        MongoRegistryOperation.ExecuteAsync<IReadOnlyList<VerificationRun>>(async () =>
        {
            var filter = Builders<VerificationRunDocument>.Filter.Eq(value => value.ConsumerApplicationId, integration.ConsumerApplicationId) &
                Builders<VerificationRunDocument>.Filter.Eq(value => value.ProviderApplicationId, integration.ProviderApplicationId) &
                Builders<VerificationRunDocument>.Filter.Eq(value => value.ContractSha256, hash);
            if (after is not null) filter &= Builders<VerificationRunDocument>.Filter.Gt(value => value.Id, after.Value.ToString("D"));
            var values = await collections.VerificationRuns.Find(filter).SortBy(value => value.Id).Limit(take).ToListAsync(cancellationToken);
            return values.Select(VerificationMapping.ToDomain).ToArray();
        });
}
