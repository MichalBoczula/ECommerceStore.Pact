namespace ECommerceStore.Pact.Domain.Contracts;

public sealed class ContractBinding
{
    public Guid Id { get; }
    public Guid SourceVersionId { get; }
    public ApplicationVersionIdentity Source { get; }
    public ContractKind Kind { get; }
    public IntegrationIdentity? Integration { get; }
    public string ArtifactSha256 { get; }
    public DateTimeOffset BoundAt { get; }
    public string IntegrationKey => Integration is null ? "" : $"{Integration.ConsumerApplicationId}:{Integration.ProviderApplicationId}";

    private ContractBinding(Guid id, Guid sourceVersionId, ApplicationVersionIdentity source, ContractKind kind,
        IntegrationIdentity? integration, string hash, DateTimeOffset boundAt)
    {
        if (id == Guid.Empty || sourceVersionId == Guid.Empty || !Enum.IsDefined(kind))
            throw new RegistryValidationException("binding", "Valid binding and source-version identities are required.");
        if (kind == ContractKind.ProviderOpenApi && integration is not null ||
            kind == ContractKind.ConsumerPact && (integration is null || integration.ConsumerApplicationId != source.ApplicationId))
            throw new RegistryValidationException("integration", "A consumer Pact must belong to its source consumer; provider OpenAPI is application-wide.");
        Id = id;
        SourceVersionId = sourceVersionId;
        Source = source;
        Kind = kind;
        Integration = integration;
        ArtifactSha256 = ContractArtifact.NormalizeHash(hash);
        BoundAt = RegistryIdentity.Timestamp(boundAt);
    }

    public static ContractBinding Create(ApplicationVersion version, ContractArtifact artifact, IntegrationIdentity? integration, DateTimeOffset now) =>
        new(Guid.NewGuid(), version.Id, version.Identity, artifact.Kind, integration, artifact.Sha256, now);

    public static ContractBinding Rehydrate(Guid id, Guid sourceVersionId, ApplicationVersionIdentity source, ContractKind kind,
        IntegrationIdentity? integration, string hash, DateTimeOffset boundAt) => new(id, sourceVersionId, source, kind, integration, hash, boundAt);
}
