using ECommerceStore.Pact.Application.Compatibility;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;
using ECommerceStore.Pact.Infrastructure.Contracts;
using ECommerceStore.Pact.Infrastructure.Registry;
using ECommerceStore.Pact.Infrastructure.Verification;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Compatibility;

internal sealed class MongoCompatibilityEvidenceReader(RegistryCollections collections) : ICompatibilityEvidenceReader
{
    public Task<ContractBinding?> GetConsumerBindingAsync(Guid versionId, IntegrationIdentity integration, CancellationToken cancellationToken) =>
        MongoRegistryOperation.ExecuteAsync(async () =>
        {
            var value = await collections.ContractBindings.Find(binding => binding.SourceVersionId == versionId.ToString("D") &&
                binding.Kind == "ConsumerPact" && binding.IntegrationKey == integration.ConsumerApplicationId + ":" + integration.ProviderApplicationId)
                .FirstOrDefaultAsync(cancellationToken);
            return value is null ? null : ContractMapping.ToDomain(value);
        });
    public Task<IReadOnlyList<VerificationRun>> FindExecutionsAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken) =>
        MongoRegistryOperation.ExecuteAsync<IReadOnlyList<VerificationRun>>(async () =>
        {
            if (keys.Count > 100) throw new InvalidOperationException("Compatibility evidence query exceeded its bound.");
            var values = await collections.VerificationRuns.Find(Builders<VerificationRunDocument>.Filter.In(value => value.ExecutionKey, keys))
                .Limit(101).ToListAsync(cancellationToken);
            if (values.Count > 100) throw new InvalidOperationException("Stored execution identities are not unique.");
            return values.Select(VerificationMapping.ToDomain).ToArray();
        });
}
