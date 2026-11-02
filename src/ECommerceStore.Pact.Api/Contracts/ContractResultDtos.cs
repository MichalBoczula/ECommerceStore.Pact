using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain.Contracts;

namespace ECommerceStore.Pact.Api.Contracts;

public sealed record ContractPublicationResultDto(Guid Id, Guid BindingId, string ArtifactSha256, string Kind, string SpecificationVersion,
    string SourceApplicationId, string CommitSha, string? ConsumerApplicationId, string ProviderApplicationId,
    string SourceRef, string BuildUrl, string PublisherId, DateTimeOffset PublishedAt, string ContentUrl)
{
    internal static ContractPublicationResultDto FromDomain(PublishedContract value) => new(value.Publication.Id, value.Binding.Id,
        value.Artifact.Sha256, value.Binding.Kind.ToString(), value.Artifact.SpecificationVersion, value.Binding.Source.ApplicationId,
        value.Binding.Source.CommitSha, value.Binding.Integration?.ConsumerApplicationId,
        value.Binding.Integration?.ProviderApplicationId ?? value.Binding.Source.ApplicationId, value.Publication.Provenance.SourceRef,
        value.Publication.Provenance.BuildUrl, value.Publication.Provenance.PublisherId, value.Publication.PublishedAt,
        $"/api/artifacts/{value.Artifact.Sha256}/content");
}

public sealed record ContractArtifactResultDto(string Sha256, string Kind, string SpecificationVersion, int Length, DateTimeOffset StoredAt, string ContentUrl)
{
    internal static ContractArtifactResultDto FromDomain(ContractArtifact value) => new(value.Sha256, value.Kind.ToString(),
        value.SpecificationVersion, value.Length, value.StoredAt, $"/api/artifacts/{value.Sha256}/content");
}
