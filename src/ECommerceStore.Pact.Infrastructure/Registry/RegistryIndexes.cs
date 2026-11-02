using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal sealed class RegistryIndexes(RegistryCollections collections) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _initialized;

    internal async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await collections.Versions.Indexes.CreateOneAsync(new CreateIndexModel<ApplicationVersionDocument>(
                Builders<ApplicationVersionDocument>.IndexKeys.Ascending(value => value.ApplicationId).Ascending(value => value.CommitSha),
                new CreateIndexOptions { Name = "uq_application_commit", Unique = true }), cancellationToken: cancellationToken);
            await collections.Integrations.Indexes.CreateOneAsync(new CreateIndexModel<IntegrationDocument>(
                Builders<IntegrationDocument>.IndexKeys.Ascending(value => value.ConsumerApplicationId)
                    .Ascending(value => value.ProviderApplicationId),
                new CreateIndexOptions { Name = "uq_consumer_provider", Unique = true }), cancellationToken: cancellationToken);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
