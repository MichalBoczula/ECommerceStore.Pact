using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Domain.Compatibility;

public sealed class CompatibilitySelection
{
    public ApplicationVersionIdentity Consumer { get; }
    public ApplicationVersionIdentity Provider { get; }
    public Guid ConsumerVersionId { get; }
    public Guid ProviderVersionId { get; }
    public Guid? BindingId { get; }
    public string ContractSha256 { get; }
    public string CiRunId { get; }
    public int CiAttempt { get; }
    public string ProviderSourceRef { get; }
    public string InputKey => VerificationValues.Hash("compatibility-selection-v1", Consumer.ApplicationId, Consumer.CommitSha,
        Provider.ApplicationId, Provider.CommitSha, ContractSha256, CiRunId, CiAttempt, ProviderSourceRef);
    public string StateKey => VerificationValues.Hash(InputKey, ConsumerVersionId, ProviderVersionId, BindingId);

    public CompatibilitySelection(ApplicationVersionIdentity consumer, ApplicationVersionIdentity provider, Guid consumerVersionId, Guid providerVersionId,
        Guid? bindingId, string contractSha256, string ciRunId, int ciAttempt, string providerSourceRef)
    {
        _ = new IntegrationIdentity(consumer.ApplicationId, provider.ApplicationId);
        if (consumerVersionId == Guid.Empty || providerVersionId == Guid.Empty || bindingId == Guid.Empty)
            throw new RegistryValidationException("versions", "Nonempty registered version/binding IDs are required.");
        if (ciAttempt is < 1 or > 10000) throw new RegistryValidationException("ciAttempt", "Use an attempt from 1 to 10000.");
        Consumer = consumer; Provider = provider; ConsumerVersionId = consumerVersionId; ProviderVersionId = providerVersionId; BindingId = bindingId;
        ContractSha256 = ContractArtifact.NormalizeHash(contractSha256); CiRunId = VerificationValues.Text(ciRunId, "ciRunId", 200);
        CiAttempt = ciAttempt; ProviderSourceRef = ContractProvenance.NormalizeSourceRef(providerSourceRef);
    }
    public string ExecutionKey(string checkId) => BindingId is null ? throw new InvalidOperationException("Contract binding is absent.") :
        VerificationRun.Identity(ConsumerVersionId, ProviderVersionId, BindingId.Value, ContractSha256, CiRunId, CiAttempt, checkId);
}
