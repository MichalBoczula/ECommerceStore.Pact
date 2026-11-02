using System.Text.Json;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Application.Verification;

public sealed class VerificationRunService(IApplicationVersionRepository versions, IContractRepository contracts,
    IVerificationRunRepository runs, TimeProvider timeProvider)
{
    public async Task<RegistrationResult<VerificationRun>> AppendAsync(VerificationRunCommand command, string publisherId, CancellationToken cancellationToken)
    {
        var consumerIdentity = new ApplicationVersionIdentity(command.ConsumerApplicationId, command.ConsumerCommitSha);
        var providerIdentity = new ApplicationVersionIdentity(command.ProviderApplicationId, command.ProviderCommitSha);
        var integration = new IntegrationIdentity(consumerIdentity.ApplicationId, providerIdentity.ApplicationId);
        var hash = ContractArtifact.NormalizeHash(command.ContractSha256);
        var publication = await contracts.GetPublicationAsync(command.ConsumerPublicationId, cancellationToken)
            ?? throw new RegistryReferenceNotFoundException("consumer_publication_not_found");
        var binding = await contracts.GetBindingAsync(publication.BindingId, cancellationToken)
            ?? throw new InvalidOperationException("Publication binding is missing.");
        if (binding.Kind != ContractKind.ConsumerPact || binding.Source != consumerIdentity || binding.Integration != integration || binding.ArtifactSha256 != hash)
            throw new RegistryValidationException("consumerPublicationId", "The publication must bind this exact consumer version, provider application and Pact hash.");
        var consumer = await versions.GetAsync(consumerIdentity, cancellationToken)
            ?? throw new RegistryReferenceNotFoundException("consumer_version_not_found");
        var provider = await versions.GetAsync(providerIdentity, cancellationToken)
            ?? throw new RegistryReferenceNotFoundException("provider_version_not_found");
        if (consumer.Id != binding.SourceVersionId) throw new InvalidOperationException("Contract source identity is inconsistent.");
        var artifact = await contracts.GetArtifactAsync(hash, cancellationToken)
            ?? throw new InvalidOperationException("Publication artifact is missing.");
        var content = artifact.GetContent();
        // Inspect coverage metadata only. Matching is performed by official Pact tooling in CI.
        using var document = JsonDocument.Parse(content.AsMemory(content.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0));
        var interactions = document.RootElement.GetProperty("interactions").EnumerateArray().ToArray();
        var names = interactions.Select(value => value.GetProperty("description").GetString()).ToHashSet(StringComparer.Ordinal);
        if (command.Issues.Any(issue => issue.Interaction is not null && !names.Contains(issue.Interaction)))
            throw new RegistryValidationException("issues.interaction", "Diagnostic interaction must belong to the referenced Pact.");
        var target = new VerificationTarget(consumer.Id, consumerIdentity, provider.Id, providerIdentity, publication.Id, binding.Id, hash);
        var execution = new VerificationExecution(command.CiRunId, command.CiAttempt, command.CiCheckId, command.VerifierName,
            command.VerifierVersion, command.ConfigurationSha256);
        var provenance = new ContractProvenance(command.SourceRef, command.BuildUrl, publisherId, provider.RepositoryUrl);
        var candidate = VerificationRun.Create(target, execution, provenance, provider.RepositoryUrl, Parse<VerificationStatus>(command.Status, "status"),
            interactions.Length, command.VerifiedInteractions, command.FailedInteractions, command.Summary, command.Issues, command.EvidenceUrls,
            command.StartedAt, command.FinishedAt, timeProvider.GetUtcNow());
        var stored = await runs.AppendAsync(candidate, cancellationToken);
        if (stored.Value.PayloadSha256 != candidate.PayloadSha256) throw new VerificationConflictException();
        return stored;
    }

    public Task<VerificationRun?> GetAsync(Guid id, CancellationToken cancellationToken) => runs.GetAsync(id, cancellationToken);

    public async Task<VerificationPage> ListAsync(string consumerApplicationId, string providerApplicationId, string contractSha256,
        int? limit, Guid? after, CancellationToken cancellationToken)
    {
        var integration = new IntegrationIdentity(consumerApplicationId, providerApplicationId);
        var hash = ContractArtifact.NormalizeHash(contractSha256);
        var size = limit ?? 50;
        if (size is < 1 or > 100 || after == Guid.Empty) throw new RegistryValidationException("pagination", "Use limit 1 to 100 and a nonempty cursor ID.");
        var values = await runs.ListAsync(integration, hash, after, size + 1, cancellationToken);
        return new VerificationPage(values.Take(size).ToArray(), values.Count > size ? values[size - 1].Id : null);
    }

    public static T Parse<T>(string? value, string field) where T : struct, Enum =>
        value is not null && Enum.GetNames<T>().Contains(value, StringComparer.OrdinalIgnoreCase) && Enum.TryParse<T>(value, true, out var parsed)
            ? parsed : throw new RegistryValidationException(field, $"Use {string.Join(", ", Enum.GetNames<T>())}.");
}
