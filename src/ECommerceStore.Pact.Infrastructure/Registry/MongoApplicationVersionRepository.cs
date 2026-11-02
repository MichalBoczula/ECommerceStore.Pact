using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Domain;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal sealed class MongoApplicationVersionRepository(RegistryCollections collections, RegistryIndexes indexes)
    : IApplicationVersionRepository
{
    public Task<RegistrationResult<ApplicationVersion>> RegisterAsync(ApplicationVersion candidate, CancellationToken cancellationToken) =>
        MongoRegistryOperation.ExecuteAsync(async () =>
        {
            await indexes.EnsureCreatedAsync(cancellationToken);
            var document = RegistryMapping.ToDocument(candidate);
            var filter = Filter(candidate.Identity);
            var update = Builders<ApplicationVersionDocument>.Update
                .SetOnInsert(value => value.Id, document.Id)
                .SetOnInsert(value => value.ApplicationId, document.ApplicationId)
                .SetOnInsert(value => value.CommitSha, document.CommitSha)
                .SetOnInsert(value => value.RepositoryUrl, document.RepositoryUrl)
                .SetOnInsert(value => value.ImageDigest, document.ImageDigest)
                .SetOnInsert(value => value.RegisteredAtUtc, document.RegisteredAtUtc);
            ApplicationVersionDocument? stored;
            try
            {
                stored = await collections.Versions.FindOneAndUpdateAsync(filter, update,
                    new FindOneAndUpdateOptions<ApplicationVersionDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
                    cancellationToken);
            }
            catch (Exception exception) when (MongoRegistryOperation.IsDuplicateKey(exception))
            {
                // Another request won the unique-key race. Return its immutable record.
                stored = await collections.Versions.Find(filter).FirstOrDefaultAsync(cancellationToken);
            }

            if (stored is null)
            {
                throw new InvalidOperationException("The registered application version could not be read.");
            }

            return new RegistrationResult<ApplicationVersion>(RegistryMapping.ToDomain(stored), stored.Id == document.Id);
        });

    public Task<ApplicationVersion?> GetAsync(ApplicationVersionIdentity identity, CancellationToken cancellationToken) =>
        MongoRegistryOperation.ExecuteAsync(async () =>
        {
            var document = await collections.Versions.Find(Filter(identity)).FirstOrDefaultAsync(cancellationToken);
            return document is null ? null : RegistryMapping.ToDomain(document);
        });

    private static FilterDefinition<ApplicationVersionDocument> Filter(ApplicationVersionIdentity identity) =>
        Builders<ApplicationVersionDocument>.Filter.Eq(value => value.ApplicationId, identity.ApplicationId) &
        Builders<ApplicationVersionDocument>.Filter.Eq(value => value.CommitSha, identity.CommitSha);
}
