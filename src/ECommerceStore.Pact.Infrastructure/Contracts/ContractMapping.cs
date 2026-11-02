using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;

namespace ECommerceStore.Pact.Infrastructure.Contracts;

internal static class ContractMapping
{
    internal static ContractArtifactDocument ToDocument(ContractArtifact value) => new()
    {
        Id = value.Sha256,
        Kind = value.Kind.ToString(),
        SpecificationVersion = value.SpecificationVersion,
        Content = value.GetContent(),
        StoredAtUtc = value.StoredAt.UtcDateTime
    };
    internal static ContractBindingDocument ToDocument(ContractBinding value) => new()
    {
        Id = value.Id.ToString("D"),
        SourceVersionId = value.SourceVersionId.ToString("D"),
        SourceApplicationId = value.Source.ApplicationId,
        CommitSha = value.Source.CommitSha,
        Kind = value.Kind.ToString(),
        IntegrationKey = value.IntegrationKey,
        ConsumerApplicationId = value.Integration?.ConsumerApplicationId,
        ProviderApplicationId = value.Integration?.ProviderApplicationId,
        ArtifactSha256 = value.ArtifactSha256,
        BoundAtUtc = value.BoundAt.UtcDateTime
    };
    internal static ContractPublicationDocument ToDocument(ContractPublication value, string repositoryUrl) => new()
    {
        Id = value.Id.ToString("D"),
        BindingId = value.BindingId.ToString("D"),
        SourceRef = value.Provenance.SourceRef,
        BuildUrl = value.Provenance.BuildUrl,
        PublisherId = value.Provenance.PublisherId,
        RepositoryUrl = repositoryUrl,
        ProvenanceHash = value.Provenance.IdentityHash,
        PublishedAtUtc = value.PublishedAt.UtcDateTime
    };
    internal static ContractArtifact ToDomain(ContractArtifactDocument value) => Read(() => ContractArtifact.Rehydrate(
        value.Id, Enum.Parse<ContractKind>(value.Kind), value.SpecificationVersion, value.Content, new DateTimeOffset(value.StoredAtUtc, TimeSpan.Zero)));
    internal static ContractBinding ToDomain(ContractBindingDocument value) => Read(() => ContractBinding.Rehydrate(
        Guid.Parse(value.Id), Guid.Parse(value.SourceVersionId), new ApplicationVersionIdentity(value.SourceApplicationId, value.CommitSha),
        Enum.Parse<ContractKind>(value.Kind), value.ConsumerApplicationId is null ? null : new IntegrationIdentity(value.ConsumerApplicationId, value.ProviderApplicationId),
        value.ArtifactSha256, new DateTimeOffset(value.BoundAtUtc, TimeSpan.Zero)));
    internal static ContractPublication ToDomain(ContractPublicationDocument value) => Read(() => ContractPublication.Rehydrate(
        Guid.Parse(value.Id), Guid.Parse(value.BindingId), new ContractProvenance(value.SourceRef, value.BuildUrl, value.PublisherId, value.RepositoryUrl),
        new DateTimeOffset(value.PublishedAtUtc, TimeSpan.Zero)));

    private static T Read<T>(Func<T> factory)
    {
        try { return factory(); }
        catch (Exception exception) when (exception is RegistryValidationException or ArgumentException or FormatException)
        { throw new InvalidOperationException("A stored contract record is invalid.", exception); }
    }
}
