using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Compatibility;
using ECommerceStore.Pact.Domain.Contracts;

namespace ECommerceStore.Pact.Application.Compatibility;

public sealed class CompatibilityDecisionService(IApplicationVersionRepository versions, IContractRepository contracts, IVerificationProfileRepository profiles,
    ICompatibilityEvidenceReader evidence, ICompatibilityDecisionRepository decisions, TimeProvider timeProvider)
{
    public async Task<RegistrationResult<CompatibilityDecision>> EvaluateAsync(EvaluateCompatibilityCommand command, string publisherId, CancellationToken cancellationToken)
    {
        if (command.Rows is null || command.Rows.Count is < 1 or > 10) throw new RegistryValidationException("rows", "Select 1 to 10 version pairs.");
        var profile = await profiles.GetAsync(command.ProfileId, cancellationToken) ?? throw new RegistryReferenceNotFoundException("verification_profile_not_found");
        var source = RegistryIdentity.ApplicationId(command.SourceApplicationId, "sourceApplicationId");
        if (source != profile.Integration.ConsumerApplicationId && source != profile.Integration.ProviderApplicationId)
            throw new RegistryValidationException("sourceApplicationId", "The requesting application must be the selected consumer or provider.");
        var selected = new List<CompatibilitySelection>();
        var knownVersions = new Dictionary<ApplicationVersionIdentity, ApplicationVersion>();
        var knownBindings = new Dictionary<Guid, ContractBinding?>();
        foreach (var row in command.Rows)
        {
            var consumer = await VersionAsync(new(profile.Integration.ConsumerApplicationId, row.ConsumerCommitSha));
            var provider = await VersionAsync(new(profile.Integration.ProviderApplicationId, row.ProviderCommitSha));
            if (!knownBindings.TryGetValue(consumer.Id, out var binding))
            {
                binding = await evidence.GetConsumerBindingAsync(consumer.Id, profile.Integration, cancellationToken);
                knownBindings[consumer.Id] = binding;
                if (binding is not null)
                {
                    if (binding.Source != consumer.Identity || binding.Kind != ContractKind.ConsumerPact || binding.Integration != profile.Integration)
                        throw new InvalidOperationException("Stored consumer binding does not match the query.");
                    var artifact = await contracts.GetArtifactAsync(binding.ArtifactSha256, cancellationToken)
                        ?? throw new InvalidOperationException("Bound consumer artifact is missing.");
                    if (artifact.Kind != ContractKind.ConsumerPact) throw new InvalidOperationException("Bound artifact is not a consumer Pact.");
                }
            }
            var hash = ContractArtifact.NormalizeHash(row.ContractSha256);
            if (binding is not null && binding.ArtifactSha256 != hash)
                throw new RegistryValidationException("contractSha256", "The selected consumer version is bound to a different Pact hash.");
            selected.Add(new CompatibilitySelection(consumer.Identity, provider.Identity, consumer.Id, provider.Id, binding?.Id, hash, row.CiRunId, row.CiAttempt, row.ProviderSourceRef));
        }
        if (selected.Select(row => row.InputKey).Distinct(StringComparer.Ordinal).Count() != selected.Count)
            throw new RegistryValidationException("rows", "Duplicate normalized version/execution selections are not supported.");
        var executionKeys = selected.Where(row => row.BindingId is not null).SelectMany(row => profile.Checks.Select(check => row.ExecutionKey(check.CheckId))).ToArray();
        var observed = executionKeys.Length == 0 ? [] : await evidence.FindExecutionsAsync(executionKeys, cancellationToken);
        var candidate = CompatibilityDecision.Evaluate(profile, selected, observed, source, publisherId, timeProvider.GetUtcNow());
        return await decisions.AppendAsync(candidate, cancellationToken);

        async Task<ApplicationVersion> VersionAsync(ApplicationVersionIdentity identity)
        {
            if (knownVersions.TryGetValue(identity, out var version)) return version;
            version = await versions.GetAsync(identity, cancellationToken) ?? throw new RegistryReferenceNotFoundException("application_version_not_found");
            knownVersions[identity] = version; return version;
        }
    }
    public Task<CompatibilityDecision?> GetAsync(Guid id, CancellationToken cancellationToken) => decisions.GetAsync(id, cancellationToken);
}
