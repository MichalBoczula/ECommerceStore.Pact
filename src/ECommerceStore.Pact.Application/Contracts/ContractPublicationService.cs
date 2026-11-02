using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;

namespace ECommerceStore.Pact.Application.Contracts;

public sealed class ContractPublicationService(IApplicationVersionRepository versions, IIntegrationRepository integrations,
    IContractRepository contracts, IContractDocumentValidator validator, TimeProvider timeProvider)
{
    public async Task<RegistrationResult<PublishedContract>> PublishAsync(ContractPublicationCommand command, string publisherId, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ContractKind>(command.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) ||
            !new[] { "ProviderOpenApi", "ConsumerPact" }.Contains(command.Kind, StringComparer.OrdinalIgnoreCase))
            throw new RegistryValidationException("kind", "Use ProviderOpenApi or ConsumerPact.");
        var identity = new ApplicationVersionIdentity(command.SourceApplicationId, command.CommitSha);
        IntegrationIdentity? integration = null;
        if (kind == ContractKind.ConsumerPact)
        {
            integration = new IntegrationIdentity(command.ConsumerApplicationId, command.ProviderApplicationId);
            if (integration.ConsumerApplicationId != identity.ApplicationId)
                throw new RegistryValidationException("sourceApplicationId", "A consumer Pact must be published by its consumer application.");
        }
        else if (command.ConsumerApplicationId is not null || command.ProviderApplicationId is not null)
            throw new RegistryValidationException("integration", "Provider OpenAPI belongs to its source application; omit consumer/provider fields.");

        if (command.ContentBase64 is null) throw new RegistryValidationException("contentBase64", "Contract content is required.");
        if (command.ContentBase64.Length > ((ContractArtifact.MaxContentBytes + 2) / 3) * 4)
            throw new ContractPayloadTooLargeException();
        byte[] content;
        try { content = Convert.FromBase64String(command.ContentBase64); }
        catch (FormatException) { throw new RegistryValidationException("contentBase64", "Provide base64 of the original UTF-8 JSON file bytes."); }
        if (content.Length > ContractArtifact.MaxContentBytes) throw new ContractPayloadTooLargeException();
        if (content.Length == 0) throw new RegistryValidationException("contentBase64", "Contract content cannot be empty.");

        var version = await versions.GetAsync(identity, cancellationToken)
            ?? throw new RegistryReferenceNotFoundException("application_version_not_found");
        if (integration is not null && await integrations.GetAsync(integration, cancellationToken) is null)
            throw new RegistryReferenceNotFoundException("integration_not_found");
        var provenance = new ContractProvenance(command.SourceRef, command.BuildUrl, publisherId, version.RepositoryUrl);
        var specificationVersion = await validator.ValidateAsync(kind, content, integration, cancellationToken);
        var artifact = ContractArtifact.Create(kind, specificationVersion, content, timeProvider.GetUtcNow());
        var binding = await contracts.BindAsync(artifact, ContractBinding.Create(version, artifact, integration, timeProvider.GetUtcNow()), cancellationToken);
        if (binding.Value.ArtifactSha256 != artifact.Sha256) throw new ContractConflictException();
        var storedArtifact = await contracts.GetArtifactAsync(artifact.Sha256, cancellationToken)
            ?? throw new InvalidOperationException("Bound contract content is missing.");
        var publication = await contracts.PublishAsync(ContractPublication.Create(binding.Value, provenance, timeProvider.GetUtcNow()), version.RepositoryUrl, cancellationToken);
        return new RegistrationResult<PublishedContract>(new PublishedContract(publication.Value, binding.Value, storedArtifact), publication.Created);
    }

    public Task<ContractArtifact?> GetArtifactAsync(string hash, CancellationToken cancellationToken) =>
        contracts.GetArtifactAsync(ContractArtifact.NormalizeHash(hash), cancellationToken);

    public async Task<PublishedContract?> GetPublicationAsync(Guid id, CancellationToken cancellationToken)
    {
        var publication = await contracts.GetPublicationAsync(id, cancellationToken);
        if (publication is null) return null;
        var binding = await contracts.GetBindingAsync(publication.BindingId, cancellationToken)
            ?? throw new InvalidOperationException("Publication binding is missing.");
        var artifact = await contracts.GetArtifactAsync(binding.ArtifactSha256, cancellationToken)
            ?? throw new InvalidOperationException("Publication artifact is missing.");
        return new PublishedContract(publication, binding, artifact);
    }
}
