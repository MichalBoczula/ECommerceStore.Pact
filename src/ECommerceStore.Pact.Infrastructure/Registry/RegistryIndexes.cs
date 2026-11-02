using ECommerceStore.Pact.Infrastructure.Contracts;
using MongoDB.Driver;
using ECommerceStore.Pact.Infrastructure.History;
using ECommerceStore.Pact.Infrastructure.Verification;

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
            await collections.ContractBindings.Indexes.CreateOneAsync(new CreateIndexModel<ContractBindingDocument>(
                Builders<ContractBindingDocument>.IndexKeys.Ascending(value => value.SourceVersionId).Ascending(value => value.Kind).Ascending(value => value.IntegrationKey),
                new CreateIndexOptions { Name = "uq_source_kind_integration", Unique = true }), cancellationToken: cancellationToken);
            await collections.ContractPublications.Indexes.CreateOneAsync(new CreateIndexModel<ContractPublicationDocument>(
                Builders<ContractPublicationDocument>.IndexKeys.Ascending(value => value.BindingId).Ascending(value => value.ProvenanceHash),
                new CreateIndexOptions { Name = "uq_binding_provenance", Unique = true }), cancellationToken: cancellationToken);
            await collections.VerificationRuns.Indexes.CreateManyAsync([
                new CreateIndexModel<VerificationRunDocument>(Builders<VerificationRunDocument>.IndexKeys.Ascending(value => value.ExecutionKey),
                    new CreateIndexOptions { Name = "uq_verification_execution", Unique = true }),
                new CreateIndexModel<VerificationRunDocument>(Builders<VerificationRunDocument>.IndexKeys.Ascending(value => value.ConsumerApplicationId)
                    .Ascending(value => value.ProviderApplicationId).Ascending(value => value.ContractSha256).Ascending(value => value.Id),
                    new CreateIndexOptions { Name = "ix_verification_contract_history" })
            ], cancellationToken);
            await collections.ContractComparisons.Indexes.CreateOneAsync(new CreateIndexModel<ContractComparisonDocument>(
                Builders<ContractComparisonDocument>.IndexKeys.Ascending(value => value.Key),
                new CreateIndexOptions { Name = "uq_contract_comparison", Unique = true }), cancellationToken: cancellationToken);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
