using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Domain;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal sealed class MongoIntegrationRepository(RegistryCollections collections, RegistryIndexes indexes) : IIntegrationRepository
{
    public Task<RegistrationResult<Integration>> RegisterAsync(Integration candidate, CancellationToken cancellationToken) =>
        MongoRegistryOperation.ExecuteAsync(async () =>
        {
            await indexes.EnsureCreatedAsync(cancellationToken);
            var document = RegistryMapping.ToDocument(candidate);
            var filter = Filter(candidate.Identity);
            var update = Builders<IntegrationDocument>.Update
                .SetOnInsert(value => value.Id, document.Id)
                .SetOnInsert(value => value.ConsumerApplicationId, document.ConsumerApplicationId)
                .SetOnInsert(value => value.ProviderApplicationId, document.ProviderApplicationId)
                .SetOnInsert(value => value.RegisteredAtUtc, document.RegisteredAtUtc);
            IntegrationDocument? stored;
            try
            {
                stored = await collections.Integrations.FindOneAndUpdateAsync(filter, update,
                    new FindOneAndUpdateOptions<IntegrationDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
                    cancellationToken);
            }
            catch (Exception exception) when (MongoRegistryOperation.IsDuplicateKey(exception))
            {
                stored = await collections.Integrations.Find(filter).FirstOrDefaultAsync(cancellationToken);
            }

            if (stored is null)
            {
                throw new InvalidOperationException("The registered integration could not be read.");
            }

            return new RegistrationResult<Integration>(RegistryMapping.ToDomain(stored), stored.Id == document.Id);
        });

    public Task<Integration?> GetAsync(IntegrationIdentity identity, CancellationToken cancellationToken) =>
        MongoRegistryOperation.ExecuteAsync(async () =>
        {
            var document = await collections.Integrations.Find(Filter(identity)).FirstOrDefaultAsync(cancellationToken);
            return document is null ? null : RegistryMapping.ToDomain(document);
        });

    private static FilterDefinition<IntegrationDocument> Filter(IntegrationIdentity identity) =>
        Builders<IntegrationDocument>.Filter.Eq(value => value.ConsumerApplicationId, identity.ConsumerApplicationId) &
        Builders<IntegrationDocument>.Filter.Eq(value => value.ProviderApplicationId, identity.ProviderApplicationId);
}
